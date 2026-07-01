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
import { ChatRoomIcon } from '../components/chat/ChatRoomIcon'
import { getCategoryIcon, getRoomIcon, getStaffRoomIcon } from '../components/chat/chatIcons'

function resolveRoomIcon(room: ChatNavRoom, categoryKey: string): ReturnType<typeof getCategoryIcon> {
  if (categoryKey === 'Staff')
    return getStaffRoomIcon(room.name)
  if (categoryKey === 'General')
    return getCategoryIcon('General')
  return getRoomIcon(room.name, categoryKey)
}

export function ChatRoom() {
  const { roomId } = useParams<{ roomId: string }>()
  const decodedRoomId = roomId ? decodeURIComponent(roomId) : ''
  const { user } = useAuth()
  const [room, setRoom] = useState<ChatNavRoom | null>(null)
  const [categoryKey, setCategoryKey] = useState<string>('General')
  const [categoryName, setCategoryName] = useState<string>('General')
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
    // useChatRoom below is already bound to the new decodedRoomId at this point, so the
    // previous room's header (name/icon/private badge) must not keep showing while this fetch
    // is in flight or if it fails — clear it immediately rather than only on success.
    setRoom(null)
    setRoomLoading(true)

    const load = async () => {
      try {
        const { data } = await chatApi.getNav()
        if (cancelled)
          return

        for (const category of data.categories) {
          const match = category.rooms.find((r) => r.id === decodedRoomId)
          if (match) {
            setRoom(match)
            setCategoryKey(category.key)
            setCategoryName(category.name)
            return
          }
        }
      } catch {
        if (!cancelled)
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

  const icon = resolveRoomIcon(room, categoryKey)
  const subtitle = room.isPrivate
    ? `${categoryName} · private`
    : `${categoryName} · public`

  return (
    <div className="chat-room-page chat-room-page--active">
      <header className="chat-room-header">
        <div className="chat-room-hero-icon chat-room-hero-icon--compact">
          <ChatRoomIcon icon={icon} isPrivate={room.isPrivate} layeredClassName="chat-room-hero-layered" />
        </div>
        <div>
          <h2>{room.name}</h2>
          <p className="chat-room-subtitle">{subtitle}</p>
        </div>
      </header>

      {messagesError && <p className="chat-room-error chat-room-error--inline">{messagesError}</p>}

      <div className="chat-room-panel">
        <ChatMessageList
          messages={messages}
          typingUsers={typingUsers}
          loading={messagesLoading}
          currentUserId={user?.userId}
        />
        {room.isPrivate && (
          <div className="chat-key-badge" aria-hidden="true">
            <ChatRoomIcon icon={icon} isPrivate layeredClassName="chat-key-badge-layered" />
          </div>
        )}
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
