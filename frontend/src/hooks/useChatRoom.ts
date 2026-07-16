import { useCallback, useEffect, useRef, useState } from 'react'
import * as signalR from '@microsoft/signalr'
import { chatApi } from '../api/chatApi'
import type { ChatMessage, ChatTypingUser, MessageVoteUpdate } from '../types/chat'
import type { SendChatMessageError } from '../types/inbox'
import { isAxiosError } from 'axios'
import { getAccessToken, getFreshAccessToken } from '../api/tokenManager'

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

  const addMessage = useCallback((message: ChatMessage) => {
    setMessages((prev) => {
      if (prev.some((existing) => existing.messageId === message.messageId))
        return prev
      return [...prev, message]
    })
  }, [])

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    setError(null)
    setMessages([])
    setReplyTarget(null)

    const load = async () => {
      try {
        const { data } = await chatApi.getMessages(roomId)
        if (cancelled)
          return
        // The SignalR connection effect below can start receiving 'ReceiveMessage' broadcasts
        // (or a locally-sent message can resolve) before this history fetch completes, since
        // both run concurrently. Merge instead of replacing so a message that arrived first
        // isn't dropped by this (possibly slower) history load overwriting the whole array.
        setMessages((prev) => {
          const byId = new Map(data.map((message) => [message.messageId, message]))
          for (const message of prev) {
            if (!byId.has(message.messageId))
              byId.set(message.messageId, message)
          }
          return Array.from(byId.values()).sort(
            (a, b) => new Date(a.createdAtUtc).getTime() - new Date(b.createdAtUtc).getTime()
          )
        })
      } catch {
        if (!cancelled)
          setError('Could not load messages for this room.')
      } finally {
        if (!cancelled)
          setLoading(false)
      }
    }

    void load()
    return () => {
      cancelled = true
    }
  }, [roomId])

  useEffect(() => {
    if (!getAccessToken())
      return

    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/chat', {
        accessTokenFactory: () => getFreshAccessToken(),
      })
      .withAutomaticReconnect()
      .build()

    connectionRef.current = connection

    connection.on('ReceiveMessage', (message: ChatMessage) => {
      addMessage(message)
    })

    if (enableVotes) {
      connection.on('MessageVoteUpdated', (payload: MessageVoteUpdate) => {
        setMessages((prev) =>
          prev.map((message) =>
            message.messageId === payload.messageId
              ? {
                  ...message,
                  score: payload.score,
                  upvoteCount: payload.upvoteCount,
                  downvoteCount: payload.downvoteCount,
                }
              : message,
          ),
        )
      })
    }

    connection.on('UserTyping', (payload: ChatTypingUser) => {
      if (payload.userId === currentUserId)
        return
      setTypingUsers((prev) => {
        if (prev.some((user) => user.userId === payload.userId))
          return prev
        return [...prev, payload]
      })
    })

    connection.on('UserStoppedTyping', (userId: string) => {
      setTypingUsers((prev) => prev.filter((user) => user.userId !== userId))
    })

    // JoinRoom (and reconnect re-join) delivers the room's current typing state because live
    // UserTyping events are only sent to others already in the group.
    connection.on('TypingUsersSnapshot', (users: ChatTypingUser[]) => {
      setTypingUsers(users.filter((user) => user.userId !== currentUserId))
    })

    const start = async () => {
      try {
        await connection.start()
        await connection.invoke('JoinRoom', roomId)
        setConnected(true)
      } catch {
        setConnected(false)
      }
    }

    void start()

    // withAutomaticReconnect() gives the client a new underlying connection after a network
    // blip, but SignalR group membership (JoinRoom's Groups.AddToGroupAsync) is tied to the
    // old connection and doesn't carry over — without rejoining here, ReceiveMessage/typing
    // broadcasts would silently stop arriving until the user navigated away and back.
    connection.onreconnected(() => {
      const wasTyping = isTypingRef.current
      if (wasTyping)
        isTypingRef.current = false

      void connection.invoke('JoinRoom', roomId).then(
        () => {
          setConnected(true)
          if (wasTyping) {
            isTypingRef.current = true
            void connection.invoke('NotifyTyping', roomId).catch(() => undefined)
          }
        },
        () => setConnected(false)
      )
    })

    connection.onreconnecting(() => {
      setConnected(false)
    })

    return () => {
      // The indicator has no server-side timeout, so an unmount/room-switch while still
      // flagged as typing must explicitly notify — otherwise it would appear "stuck" for
      // everyone else in the room (the hub also clears it on disconnect as a safety net).
      if (isTypingRef.current) {
        isTypingRef.current = false
        void connection.invoke('NotifyStoppedTyping', roomId).catch(() => undefined)
      }
      void connection.invoke('LeaveRoom', roomId).catch(() => undefined)
      void connection.stop()
      connectionRef.current = null
      setConnected(false)
      setTypingUsers([])
    }
  }, [roomId, currentUserId, addMessage, enableVotes])

  const stopTyping = useCallback(() => {
    if (isTypingRef.current) {
      isTypingRef.current = false
      void connectionRef.current?.invoke('NotifyStoppedTyping', roomId).catch(() => undefined)
    }
  }, [roomId])

  // The indicator is meant to persist for as long as the composer has text in it, so this
  // only needs to notify once per typing burst (no idle timeout) — stopTyping is called
  // explicitly whenever the composer is cleared, blurred with no text, or the message is sent.
  const notifyTyping = useCallback(() => {
    if (!isTypingRef.current) {
      isTypingRef.current = true
      void connectionRef.current?.invoke('NotifyTyping', roomId).catch(() => undefined)
    }
  }, [roomId])

  const sendMessage = useCallback(async (content: string) => {
    const trimmed = content.trim()
    if (!trimmed || sending)
      return false

    const replyToMessageId = replyTarget?.messageId
    setSending(true)
    stopTyping()
    try {
      const { data } = await chatApi.sendMessage(roomId, trimmed, replyToMessageId)
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
      setMessages((prev) =>
        prev.map((item) =>
          item.messageId === data.messageId
            ? {
                ...item,
                score: data.score,
                upvoteCount: data.upvoteCount,
                downvoteCount: data.downvoteCount,
                viewerVote: data.viewerVote === 'up' || data.viewerVote === 'down'
                  ? data.viewerVote
                  : null,
              }
            : item,
        ),
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
