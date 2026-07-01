import { useCallback, useEffect, useRef, useState } from 'react'
import * as signalR from '@microsoft/signalr'
import { chatApi } from '../api/chatApi'
import type { ChatMessage, ChatTypingUser } from '../types/chat'

const TYPING_IDLE_MS = 2500

export function useChatRoom(roomId: string, currentUserId: string | undefined) {
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [sending, setSending] = useState(false)
  const [typingUsers, setTypingUsers] = useState<ChatTypingUser[]>([])
  const [connected, setConnected] = useState(false)

  const connectionRef = useRef<signalR.HubConnection | null>(null)
  const idleTimerRef = useRef<number | null>(null)
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
        if (!cancelled)
          setMessages(data)
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

    return () => {
      void connection.invoke('LeaveRoom', roomId).catch(() => undefined)
      void connection.stop()
      connectionRef.current = null
      setConnected(false)
      setTypingUsers([])
    }
  }, [roomId, currentUserId, addMessage])

  const stopTyping = useCallback(() => {
    if (idleTimerRef.current !== null) {
      window.clearTimeout(idleTimerRef.current)
      idleTimerRef.current = null
    }
    if (isTypingRef.current) {
      isTypingRef.current = false
      void connectionRef.current?.invoke('NotifyStoppedTyping', roomId).catch(() => undefined)
    }
  }, [roomId])

  // Notify immediately on the first keystroke of a burst, then keep pushing the idle
  // timeout back on every subsequent keystroke. A pure debounce (send only once typing
  // pauses) would mean continuous typing never fires the event at all.
  const notifyTyping = useCallback(() => {
    if (!isTypingRef.current) {
      isTypingRef.current = true
      void connectionRef.current?.invoke('NotifyTyping', roomId).catch(() => undefined)
    }

    if (idleTimerRef.current !== null)
      window.clearTimeout(idleTimerRef.current)

    idleTimerRef.current = window.setTimeout(() => {
      stopTyping()
    }, TYPING_IDLE_MS)
  }, [roomId, stopTyping])

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
      return false
    } finally {
      setSending(false)
    }
  }, [roomId, sending, addMessage, stopTyping])

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
