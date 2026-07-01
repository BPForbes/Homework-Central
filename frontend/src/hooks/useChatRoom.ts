import { useCallback, useEffect, useRef, useState } from 'react'
import * as signalR from '@microsoft/signalr'
import { chatApi } from '../api/chatApi'
import type { ChatMessage, ChatTypingUser } from '../types/chat'

export function useChatRoom(roomId: string, currentUserId: string | undefined) {
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [sending, setSending] = useState(false)
  const [typingUsers, setTypingUsers] = useState<ChatTypingUser[]>([])
  const [connected, setConnected] = useState(false)

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
    const token = sessionStorage.getItem('accessToken')
    if (!token)
      return

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`/hubs/chat?access_token=${encodeURIComponent(token)}`)
      .withAutomaticReconnect()
      .build()

    connectionRef.current = connection

    connection.on('ReceiveMessage', (message: ChatMessage) => {
      addMessage(message)
    })

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
  }, [roomId, currentUserId, addMessage])

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

    setSending(true)
    stopTyping()
    try {
      const { data } = await chatApi.sendMessage(roomId, trimmed)
      addMessage(data)
      return true
    } catch {
      setError('Failed to send message.')
      notifyTyping()
      return false
    } finally {
      setSending(false)
    }
  }, [roomId, sending, addMessage, stopTyping, notifyTyping])

  return {
    messages,
    loading,
    error,
    sending,
    typingUsers,
    connected,
    sendMessage,
    notifyTyping,
    stopTyping,
  }
}
