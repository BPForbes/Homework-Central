import { Navigate } from 'react-router-dom'
import { useEffect, useState } from 'react'
import { chatApi } from '../api/chatApi'

/** Redirects to the first available chat room, or dashboard if none. */
export function ChatIndex() {
  const [target, setTarget] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    let cancelled = false

    const load = async () => {
      try {
        const { data } = await chatApi.getNav()
        if (cancelled)
          return

        const generalRoom = data.categories.find((c) => c.key === 'General')?.rooms[0]
        const firstRoom = generalRoom ?? data.categories.flatMap((c) => c.rooms)[0]
        setTarget(firstRoom ? `/chat/${encodeURIComponent(firstRoom.id)}` : '/dashboard')
      } catch {
        if (!cancelled)
          setTarget('/dashboard')
      } finally {
        if (!cancelled)
          setLoading(false)
      }
    }

    void load()
    return () => {
      cancelled = true
    }
  }, [])

  if (loading)
    return <p className="chat-room-status">Loading chats…</p>

  return <Navigate to={target ?? '/dashboard'} replace />
}
