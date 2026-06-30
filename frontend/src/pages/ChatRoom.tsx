import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faArrowLeft, faComments } from '@fortawesome/free-solid-svg-icons'
import { chatApi } from '../api/chatApi'
import type { ChatNavRoom } from '../types/chat'
import { getRoomIcon, getStaffRoomIcon } from '../components/chat/chatIcons'

export function ChatRoom() {
  const { roomId } = useParams<{ roomId: string }>()
  const decodedRoomId = roomId ? decodeURIComponent(roomId) : ''
  const [room, setRoom] = useState<ChatNavRoom | null>(null)
  const [categoryKey, setCategoryKey] = useState<string>('Staff')
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    let cancelled = false

    const load = async () => {
      setLoading(true)
      try {
        const { data } = await chatApi.getNav()
        if (cancelled)
          return

        for (const category of data.categories) {
          const match = category.rooms.find((r) => r.id === decodedRoomId)
          if (match) {
            setRoom(match)
            setCategoryKey(category.key)
            return
          }
        }
        setRoom(null)
      } finally {
        if (!cancelled)
          setLoading(false)
      }
    }

    void load()
    return () => {
      cancelled = true
    }
  }, [decodedRoomId])

  if (loading) {
    return (
      <div className="chat-room-page">
        <p className="chat-room-status">Loading chat…</p>
      </div>
    )
  }

  if (!room) {
    return (
      <div className="chat-room-page">
        <p className="chat-room-error">This chat room is not available.</p>
        <Link to="/dashboard" className="chat-room-back">
          <FontAwesomeIcon icon={faArrowLeft} /> Back to dashboard
        </Link>
      </div>
    )
  }

  const isStaff = categoryKey === 'Staff'
  const icon = isStaff ? getStaffRoomIcon(room.name) : getRoomIcon(room.name, categoryKey)

  return (
    <div className="chat-room-page">
      <div className="chat-room-hero">
        <div className="chat-room-hero-icon">
          <FontAwesomeIcon icon={icon} />
        </div>
        <div>
          <h2>{room.name}</h2>
          <p className="chat-room-subtitle">Messaging for this room is coming soon.</p>
        </div>
      </div>
      <div className="chat-room-placeholder">
        <FontAwesomeIcon icon={faComments} className="chat-room-placeholder-icon" />
        <p>Select a room from the Chats menu to switch channels.</p>
      </div>
    </div>
  )
}
