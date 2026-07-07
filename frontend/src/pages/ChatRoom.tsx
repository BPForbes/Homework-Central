import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faArrowLeft } from '@fortawesome/free-solid-svg-icons'
import { chatApi } from '../api/chatApi'
import type { ChatRoomDetail } from '../types/chat'
import { useAuth } from '../context/AuthContext'
import { useChatRoom } from '../hooks/useChatRoom'
import { ChatComposer } from '../components/chat/ChatComposer'
import { ChatMessageList } from '../components/chat/ChatMessageList'
import { ChatRoomIcon } from '../components/chat/ChatRoomIcon'
import {
  CustomInfoRoomPanel,
  CustomRoleClaimPanel,
} from '../components/infrastructure/CustomRoomPanels'
import { GetRolesPanel } from '../components/infrastructure/GetRolesPanel'
import { ServerMaintenanceNav } from '../components/layout/ServerMaintenanceNav'
import { getCategoryIcon, getRoomIcon, getStaffRoomIcon } from '../components/chat/chatIcons'
import { resolveCustomRoomIcon } from '../components/infrastructure/customRoomIcons'

function resolveRoomIcon(room: ChatRoomDetail): ReturnType<typeof getCategoryIcon> {
  if (room.iconName)
    return resolveCustomRoomIcon(room.iconName, room.roomType as 'Chat' | 'Info' | 'RoleClaim')

  if (room.categoryKey === 'Staff')
    return getStaffRoomIcon(room.name)
  if (room.roomType === 'Info')
    return resolveCustomRoomIcon(null, 'Info')
  if (room.roomType === 'RoleClaim' || room.roomType === 'GetRoles')
    return resolveCustomRoomIcon(null, 'RoleClaim')
  if (room.categoryKind === 'Custom')
    return resolveCustomRoomIcon(null, 'Chat')
  if (room.categoryKey === 'General')
    return getCategoryIcon('General')
  return getRoomIcon(room.name, room.categoryKey)
}

function navTitleForRoomType(roomType: string): string {
  switch (roomType) {
    case 'Info':
      return 'Info'
    case 'RoleClaim':
    case 'GetRoles':
      return 'Role Claim'
    default:
      return 'Chat'
  }
}

export function ChatRoom() {
  const { roomId } = useParams<{ roomId: string }>()
  const decodedRoomId = roomId ? decodeURIComponent(roomId) : ''
  const { user } = useAuth()
  const [room, setRoom] = useState<ChatRoomDetail | null>(null)
  const [roomLoading, setRoomLoading] = useState(true)
  const [accessDenied, setAccessDenied] = useState(false)

  const roomType = room?.roomType ?? 'Chat'
  const isChatRoom = roomType === 'Chat'

  const {
    messages,
    loading: messagesLoading,
    error: messagesError,
    sending,
    typingUsers,
    sendMessage,
    notifyTyping,
    stopTyping,
  } = useChatRoom(isChatRoom ? decodedRoomId : '', user?.userId)

  useEffect(() => {
    let cancelled = false
    setRoom(null)
    setRoomLoading(true)
    setAccessDenied(false)

    void chatApi
      .getRoom(decodedRoomId)
      .then(({ data }) => {
        if (!cancelled) setRoom(data)
      })
      .catch((err: unknown) => {
        if (!cancelled) {
          const status =
            typeof err === 'object' && err !== null && 'response' in err
              ? (err as { response?: { status?: number } }).response?.status
              : undefined
          setAccessDenied(status === 403)
          setRoom(null)
        }
      })
      .finally(() => {
        if (!cancelled) setRoomLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [decodedRoomId])

  if (roomLoading) {
    return (
      <div className="chat-room-page">
        <p className="chat-room-status">Loading…</p>
      </div>
    )
  }

  if (!room) {
    return (
      <div className="chat-room-page">
        <p className="chat-room-error">
          {accessDenied ? 'You do not have access to this room.' : 'This room is not available.'}
        </p>
        <Link to="/dashboard" className="chat-room-back">
          <FontAwesomeIcon icon={faArrowLeft} /> Back to dashboard
        </Link>
      </div>
    )
  }

  const icon = resolveRoomIcon(room)
  const subtitle = room.isPrivate
    ? `${room.categoryDisplayName} · private · ${roomType}`
    : `${room.categoryDisplayName} · public · ${roomType}`

  return (
    <div className="chat-room-page chat-room-page--active">
      <ServerMaintenanceNav title={navTitleForRoomType(roomType)} />

      <header className="chat-room-header">
        <Link to="/dashboard" className="chat-room-header-back" aria-label="Back to dashboard" title="Back to dashboard">
          <FontAwesomeIcon icon={faArrowLeft} />
        </Link>
        <div className="chat-room-hero-icon chat-room-hero-icon--compact">
          <ChatRoomIcon icon={icon} isPrivate={room.isPrivate} layeredClassName="chat-room-hero-layered" />
        </div>
        <div>
          <h2>{room.name}</h2>
          <p className="chat-room-subtitle">{subtitle}</p>
        </div>
      </header>

      {roomType === 'Info' && (
        <CustomInfoRoomPanel
          room={room}
          onUpdated={(content) => setRoom((prev) => (prev ? { ...prev, infoContent: content } : prev))}
        />
      )}

      {(roomType === 'RoleClaim' || roomType === 'GetRoles') &&
        (roomType === 'GetRoles' ? (
          <GetRolesPanel roomId={decodedRoomId} />
        ) : (
          <CustomRoleClaimPanel roomId={decodedRoomId} />
        ))}

      {isChatRoom && (
        <>
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
        </>
      )}
    </div>
  )
}
