import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faArrowLeft } from '@fortawesome/free-solid-svg-icons'
import { chatApi } from '../api/chatApi'
import type { ChatRoomDetail, MentionRoleOption } from '../types/chat'
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
  if (room.iconName) {
    const roomType = room.roomType === 'GetRoles' ? 'RoleClaim' : room.roomType
    if (roomType === 'Chat' || roomType === 'Info' || roomType === 'RoleClaim')
      return resolveCustomRoomIcon(room.iconName, roomType)
  }

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
  const [mentionRoles, setMentionRoles] = useState<MentionRoleOption[]>([])

  const roomType = room?.roomType ?? 'Chat'
  const isChatRoom = roomType === 'Chat'

  const {
    messages,
    loading: messagesLoading,
    error: messagesError,
    sending,
    typingUsers,
    replyTarget,
    sendMessage,
    notifyTyping,
    stopTyping,
    startReply,
    cancelReply,
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

  useEffect(() => {
    if (!isChatRoom)
      return

    let cancelled = false
    void chatApi
      .getMentionRoles()
      .then(({ data }) => {
        if (!cancelled) setMentionRoles(data)
      })
      .catch(() => {
        if (!cancelled) setMentionRoles([])
      })

    return () => {
      cancelled = true
    }
  }, [decodedRoomId, isChatRoom])

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
    ? `${room.categoryDisplayName} · private · ${navTitleForRoomType(roomType)}`
    : `${room.categoryDisplayName} · public · ${navTitleForRoomType(roomType)}`

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
              mentionRoles={mentionRoles}
              onReply={startReply}
            />
            {room.isPrivate && (
              <div className="chat-key-badge" aria-hidden="true">
                <ChatRoomIcon icon={icon} isPrivate layeredClassName="chat-key-badge-layered" />
              </div>
            )}
            <ChatComposer
              disabled={messagesLoading}
              sending={sending}
              replyTarget={replyTarget}
              messages={messages}
              mentionRoles={mentionRoles}
              onSend={sendMessage}
              onTyping={notifyTyping}
              onStopTyping={stopTyping}
              onCancelReply={cancelReply}
            />
          </div>
        </>
      )}
    </div>
  )
}
