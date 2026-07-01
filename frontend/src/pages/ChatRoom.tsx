import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faArrowLeft } from '@fortawesome/free-solid-svg-icons'
import { chatApi } from '../api/chatApi'
import type { ChatNavRoom } from '../types/chat'
import { useAuth } from '../context/AuthContext'
import { useChatRoom } from '../hooks/useChatRoom'
import { ChatComposer } from '../components/chat/ChatComposer'
import { ChatMessageList } from '../components/chat/ChatMessageList'
import { ChatRoomKeyBadge } from '../components/chat/ChatRoomKeyBadge'
import { getRoomIcon, getStaffRoomIcon } from '../components/chat/chatIcons'

export function ChatRoom() {
  const { roomId } = useParams<{ roomId: string }>()
  const decodedRoomId = roomId ? decodeURIComponent(roomId) : ''
  const { user } = useAuth()
  const [room, setRoom] = useState<ChatNavRoom | null>(null)
  const [categoryKey, setCategoryKey] = useState<string>('Staff')
  const [roomLoading, setRoomLoading] = useState(true)

  const {
    messages,
    loading: messagesLoading,
    error: messagesError,
    sending,
    typingUsers,
    sendMessage,
    notifyTyping,
    stopTyping,
  } = useChatRoom(decodedRoomId, user?.userId)

  useEffect(() => {
    let cancelled = false

    const load = async () => {
      setRoomLoading(true)
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
          setRoomLoading(false)
      }
    }

    void load()
    return () => {
      cancelled = true
    }
  }, [decodedRoomId])

  if (roomLoading) {
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
    <div className="chat-room-page chat-room-page--active">
      <header className="chat-room-header">
        <div className="chat-room-hero-icon chat-room-hero-icon--compact">
          <FontAwesomeIcon icon={icon} />
        </div>
        <div>
          <h2>{room.name}</h2>
          <p className="chat-room-subtitle">{categoryKey === 'Staff' ? 'Staff channel' : categoryKey}</p>
        </div>
      </header>

      {messagesError && <p className="chat-room-error chat-room-error--inline">{messagesError}</p>}

      <div className="chat-room-panel">
        <ChatMessageList
          messages={messages}
          typingUsers={typingUsers}
          loading={messagesLoading}
        />
        <ChatRoomKeyBadge roomIcon={icon} />
        <ChatComposer
          disabled={messagesLoading}
          sending={sending}
          onSend={sendMessage}
          onTyping={notifyTyping}
          onStopTyping={stopTyping}
        />
      </div>
    </div>
  )
}
