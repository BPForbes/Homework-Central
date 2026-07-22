import { useCallback, useEffect, useRef, useState } from 'react'
import type { Dispatch, MutableRefObject, SetStateAction } from 'react'
import * as signalR from '@microsoft/signalr'
import { chatApi } from '../api/chatApi'
import type { ChatMessage, ChatTypingUser, MessageVoteUpdate } from '../types/chat'
import type { SendChatMessageError } from '../types/inbox'
import { isAxiosError } from 'axios'
import { getAccessToken, getFreshAccessToken } from '../api/tokenManager'

// Bound long-lived room tabs; older history can be fetched again through pagination.
const MAX_RETAINED_MESSAGES = 250

type MessageSetter = Dispatch<SetStateAction<ChatMessage[]>>
type StringSetter = Dispatch<SetStateAction<string | null>>
type BooleanSetter = Dispatch<SetStateAction<boolean>>
type TypingUsersSetter = Dispatch<SetStateAction<ChatTypingUser[]>>

interface SignalRHandlerOptions {
  currentUserId: string | undefined
  enableVotes: boolean
  addMessage: (message: ChatMessage) => void
  setMessages: MessageSetter
  setTypingUsers: TypingUsersSetter
}

function sortAndRetainMessages(messages: ChatMessage[]): ChatMessage[] {
  return [...messages]
    .sort((a, b) => new Date(a.createdAtUtc).getTime() - new Date(b.createdAtUtc).getTime())
    .slice(-MAX_RETAINED_MESSAGES)
}

function mergeServerHistoryMessages(
  serverMessages: ChatMessage[],
  locallyReceivedMessages: ChatMessage[],
): ChatMessage[] {
  const messagesById = new Map(serverMessages.map((message) => [message.messageId, message]))
  for (const message of locallyReceivedMessages) {
    if (!messagesById.has(message.messageId))
      messagesById.set(message.messageId, message)
  }

  // Server timestamps remain authoritative when HTTP history and SignalR delivery
  // race each other for the same room. See docs/chat.md.
  return sortAndRetainMessages(Array.from(messagesById.values()))
}

function rebuildKnownMessageIds(knownMessageIds: Set<string>, messages: ChatMessage[]) {
  knownMessageIds.clear()
  for (const message of messages)
    knownMessageIds.add(message.messageId)
}

// Live SignalR appends check the Set instead of scanning the messages array.
// Cap rebuild keeps membership aligned after the 250-message retain trim.
// Time: O(1) membership; trim rebuild O(M). Space: O(M) capped at 250. See docs/runtime.md.
function appendLiveMessage(
  previousMessages: ChatMessage[],
  message: ChatMessage,
  knownMessageIds: Set<string>,
): ChatMessage[] {
  // Fail-first: skip when history, own send, or a prior live event already recorded the id.
  if (knownMessageIds.has(message.messageId))
    return previousMessages

  const nextMessages = [...previousMessages, message]
  if (nextMessages.length <= MAX_RETAINED_MESSAGES) {
    knownMessageIds.add(message.messageId)
    return nextMessages
  }

  const retainedMessages = nextMessages.slice(-MAX_RETAINED_MESSAGES)
  rebuildKnownMessageIds(knownMessageIds, retainedMessages)
  return retainedMessages
}

function applyBroadcastVoteUpdate(message: ChatMessage, payload: MessageVoteUpdate): ChatMessage {
  if (message.messageId !== payload.messageId)
    return message

  return {
    ...message,
    score: payload.score,
    upvoteCount: payload.upvoteCount,
    downvoteCount: payload.downvoteCount,
  }
}

function applyViewerVoteUpdate(message: ChatMessage, payload: MessageVoteUpdate): ChatMessage {
  if (message.messageId !== payload.messageId)
    return message

  return {
    ...message,
    score: payload.score,
    upvoteCount: payload.upvoteCount,
    downvoteCount: payload.downvoteCount,
    viewerVote: payload.viewerVote === 'up' || payload.viewerVote === 'down'
      ? payload.viewerVote
      : null,
  }
}

async function loadRoomHistory(
  roomId: string,
  isCancelled: () => boolean,
  setMessages: MessageSetter,
  setError: StringSetter,
  setLoading: BooleanSetter,
  knownMessageIds: Set<string>,
) {
  try {
    const { data } = await chatApi.getMessages(roomId)
    if (isCancelled())
      return

    setMessages((previousMessages) => {
      const mergedMessages = mergeServerHistoryMessages(data, previousMessages)
      // Seed membership from REST history before further SignalR appends.
      rebuildKnownMessageIds(knownMessageIds, mergedMessages)
      return mergedMessages
    })
  } catch {
    if (!isCancelled())
      setError('Could not load messages for this room.')
  } finally {
    if (!isCancelled())
      setLoading(false)
  }
}

function buildChatConnection(): signalR.HubConnection {
  return new signalR.HubConnectionBuilder()
    .withUrl('/hubs/chat', {
      accessTokenFactory: () => getFreshAccessToken(),
    })
    .withAutomaticReconnect()
    .build()
}

function registerSignalRHandlers(
  connection: signalR.HubConnection,
  options: SignalRHandlerOptions,
) {
  connection.on('ReceiveMessage', (message: ChatMessage) => {
    options.addMessage(message)
  })

  if (options.enableVotes) {
    connection.on('MessageVoteUpdated', (payload: MessageVoteUpdate) => {
      options.setMessages((previousMessages) =>
        previousMessages.map((message) => applyBroadcastVoteUpdate(message, payload)),
      )
    })
  }

  connection.on('UserTyping', (payload: ChatTypingUser) => {
    if (payload.userId === options.currentUserId)
      return

    options.setTypingUsers((previousUsers) => {
      if (previousUsers.some((user) => user.userId === payload.userId))
        return previousUsers
      return [...previousUsers, payload]
    })
  })

  connection.on('UserStoppedTyping', (userId: string) => {
    options.setTypingUsers((previousUsers) => previousUsers.filter((user) => user.userId !== userId))
  })

  // JoinRoom snapshots close the gap for users who started typing before the
  // current connection entered the SignalR group.
  connection.on('TypingUsersSnapshot', (users: ChatTypingUser[]) => {
    options.setTypingUsers(users.filter((user) => user.userId !== options.currentUserId))
  })
}

async function startRoomConnection(
  connection: signalR.HubConnection,
  roomId: string,
  setConnected: BooleanSetter,
) {
  try {
    await connection.start()
    await connection.invoke('JoinRoom', roomId)
    setConnected(true)
  } catch {
    setConnected(false)
  }
}

async function rejoinRoomConnection(
  connection: signalR.HubConnection,
  roomId: string,
  isTypingRef: MutableRefObject<boolean>,
  setConnected: BooleanSetter,
) {
  const wasTyping = isTypingRef.current
  if (wasTyping)
    isTypingRef.current = false

  try {
    await connection.invoke('JoinRoom', roomId)
    setConnected(true)
    if (wasTyping) {
      isTypingRef.current = true
      void connection.invoke('NotifyTyping', roomId).catch(() => undefined)
    }
  } catch {
    setConnected(false)
  }
}

function registerReconnectHandlers(
  connection: signalR.HubConnection,
  roomId: string,
  isTypingRef: MutableRefObject<boolean>,
  setConnected: BooleanSetter,
) {
  connection.onreconnected(() => {
    void rejoinRoomConnection(connection, roomId, isTypingRef, setConnected)
  })

  connection.onreconnecting(() => {
    setConnected(false)
    // Automatic reconnect opens a new connection id; clearing typing on the
    // dying connection avoids a stale indicator if hub disconnect cleanup lags.
    if (isTypingRef.current) {
      isTypingRef.current = false
      void connection.invoke('NotifyStoppedTyping', roomId).catch(() => undefined)
    }
  })
}

function cleanupRoomConnection(
  connection: signalR.HubConnection,
  roomId: string,
  isTypingRef: MutableRefObject<boolean>,
  setConnected: BooleanSetter,
  setTypingUsers: TypingUsersSetter,
) {
  // The indicator has no server-side timeout, so unmounts and room switches must
  // explicitly clear active typing state for other viewers.
  if (isTypingRef.current) {
    isTypingRef.current = false
    void connection.invoke('NotifyStoppedTyping', roomId).catch(() => undefined)
  }

  void connection.invoke('LeaveRoom', roomId).catch(() => undefined)
  void connection.stop()
  setConnected(false)
  setTypingUsers([])
}

export function useChatRoom(
  roomId: string,
  currentUserId: string | undefined,
  options?: { enableVotes?: boolean },
) {
  const enableVotes = options?.enableVotes ?? true
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [sending, setSending] = useState(false)
  const [typingUsers, setTypingUsers] = useState<ChatTypingUser[]>([])
  const [connected, setConnected] = useState(false)
  const [replyTarget, setReplyTarget] = useState<ChatMessage | null>(null)

  const connectionRef = useRef<signalR.HubConnection | null>(null)
  const isTypingRef = useRef(false)
  // Membership set for live dedupe (O(1) has/add); see appendLiveMessage and docs/runtime.md.
  const knownMessageIdsRef = useRef<Set<string>>(new Set())

  const addMessage = useCallback((message: ChatMessage) => {
    setMessages((previousMessages) => {
      const nextMessages = appendLiveMessage(previousMessages, message, knownMessageIdsRef.current)
      return nextMessages
    })
  }, [])

  useEffect(() => {
    let cancelled = false
    setLoading(Boolean(roomId))
    setError(null)
    setMessages([])
    setReplyTarget(null)
    // Room switch: drop prior ids so a reused message id cannot suppress a fresh history row.
    knownMessageIdsRef.current = new Set()

    if (!roomId) {
      setLoading(false)
      return
    }

    void loadRoomHistory(
      roomId,
      () => cancelled,
      setMessages,
      setError,
      setLoading,
      knownMessageIdsRef.current,
    )
    return () => {
      cancelled = true
    }
  }, [roomId])

  useEffect(() => {
    if (!roomId || !getAccessToken())
      return

    const connection = buildChatConnection()
    connectionRef.current = connection

    registerSignalRHandlers(connection, {
      currentUserId,
      enableVotes,
      addMessage,
      setMessages,
      setTypingUsers,
    })
    registerReconnectHandlers(connection, roomId, isTypingRef, setConnected)

    void startRoomConnection(connection, roomId, setConnected)

    return () => {
      cleanupRoomConnection(connection, roomId, isTypingRef, setConnected, setTypingUsers)
      connectionRef.current = null
    }
  }, [roomId, currentUserId, addMessage, enableVotes])

  const stopTyping = useCallback(() => {
    if (isTypingRef.current) {
      isTypingRef.current = false
      void connectionRef.current?.invoke('NotifyStoppedTyping', roomId).catch(() => undefined)
    }
  }, [roomId])

  // Typing persists while the composer contains text. A typing burst sends one
  // notification; clearing, blurring an empty composer, and sending stop it explicitly.
  const notifyTyping = useCallback(() => {
    if (!isTypingRef.current) {
      isTypingRef.current = true
      void connectionRef.current?.invoke('NotifyTyping', roomId).catch(() => undefined)
    }
  }, [roomId])

  const sendMessage = useCallback(async (content: string, attachmentIds?: string[]) => {
    const trimmed = content.trim()
    const hasAttachments = Boolean(attachmentIds && attachmentIds.length > 0)
    if ((!trimmed && !hasAttachments) || sending)
      return false

    const replyToMessageId = replyTarget?.messageId
    setSending(true)
    stopTyping()
    try {
      const { data } = await chatApi.sendMessage(roomId, trimmed, replyToMessageId, {
        attachmentIds: hasAttachments ? attachmentIds : undefined,
      })
      addMessage(data)
      setReplyTarget(null)
      return true
    } catch (err) {
      if (isAxiosError<SendChatMessageError>(err) && err.response?.data?.message) {
        setError(err.response.data.message)
      } else {
        setError('Failed to send message.')
      }
      notifyTyping()
      return false
    } finally {
      setSending(false)
    }
  }, [roomId, sending, replyTarget, addMessage, stopTyping, notifyTyping])

  const startReply = useCallback((message: ChatMessage) => {
    setReplyTarget(message)
  }, [])

  const cancelReply = useCallback(() => {
    setReplyTarget(null)
  }, [])

  const castVote = useCallback(async (message: ChatMessage, value: 1 | -1) => {
    if (!enableVotes)
      return
    try {
      const { data } = await chatApi.castVote(message.messageId, value)
      setMessages((previousMessages) =>
        previousMessages.map((item) => applyViewerVoteUpdate(item, data)),
      )
    } catch {
      setError('Could not update vote.')
    }
  }, [enableVotes])

  return {
    messages,
    loading,
    error,
    sending,
    typingUsers,
    connected,
    replyTarget,
    sendMessage,
    notifyTyping,
    stopTyping,
    startReply,
    cancelReply,
    castVote,
  }
}
