import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faArrowLeft } from '@fortawesome/free-solid-svg-icons'
import { chatApi } from '../api/chatApi'
import { infrastructureApi } from '../api/infrastructureApi'
import type { ChatNavRoom } from '../types/chat'
import { useAuth } from '../context/AuthContext'
import { MANAGE_SERVER_INFRASTRUCTURE_BIT } from '../constants/permissions'
import { useChatRoom } from '../hooks/useChatRoom'
import { ChatComposer } from '../components/chat/ChatComposer'
import { ChatMessageList } from '../components/chat/ChatMessageList'
import { ChatRoomIcon } from '../components/chat/ChatRoomIcon'
import { CustomInfoRoomPanel, CustomRoleClaimPanel } from '../components/infrastructure/CustomRoomPanels'
import { ServerMaintenanceNav } from '../components/layout/ServerMaintenanceNav'
import { getCategoryIcon, getRoomIcon, getStaffRoomIcon } from '../components/chat/chatIcons'
import type { CustomChannel } from '../types/infrastructure'

function resolveRoomIcon(room: ChatNavRoom, categoryKey: string): ReturnType<typeof getCategoryIcon> {
  if (categoryKey === 'Staff')
    return getStaffRoomIcon(room.name)
  if (categoryKey === 'General')
    return getCategoryIcon('General')
  if (categoryKey === 'Custom')
    return getCategoryIcon('General')
  return getRoomIcon(room.name, categoryKey)
}

export function ChatRoom() {
  const { roomId } = useParams<{ roomId: string }>()
  const decodedRoomId = roomId ? decodeURIComponent(roomId) : ''
  const { user, hasPermission } = useAuth()
  const [room, setRoom] = useState<ChatNavRoom | null>(null)
  const [categoryKey, setCategoryKey] = useState<string>('General')
  const [categoryName, setCategoryName] = useState<string>('General')
  const [roomLoading, setRoomLoading] = useState(true)
  const [customChannel, setCustomChannel] = useState<CustomChannel | null>(null)

  const roomType = room?.roomType ?? customChannel?.roomType ?? 'Chat'
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
    setCustomChannel(null)
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
            if (match.roomType && match.roomType !== 'Chat') {
              try {
                const channelRes = await infrastructureApi.getChannelByRoom(decodedRoomId)
                if (!cancelled) setCustomChannel(channelRes.data)
              } catch {
                // Info/claim metadata optional if nav already identified type.
              }
            }
            return
          }
        }

        if (decodedRoomId.startsWith('custom:')) {
          try {
            const channelRes = await infrastructureApi.getChannelByRoom(decodedRoomId)
            if (cancelled) return
            const ch = channelRes.data
            setCustomChannel(ch)
            setRoom({
              id: ch.roomId,
              name: ch.displayName,
              isPrivate: ch.isPrivate,
              categoryKey: ch.categoryKey,
              categoryKind: 'Custom',
              roomType: ch.roomType,
            })
            setCategoryKey(ch.categoryKey)
            setCategoryName(ch.categoryDisplayName)
          } catch {
            if (!cancelled) setRoom(null)
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
        <p className="chat-room-status">Loading…</p>
      </div>
    )
  }

  if (!room) {
    return (
      <div className="chat-room-page">
        <p className="chat-room-error">This room is not available.</p>
        <Link to="/dashboard" className="chat-room-back">
          <FontAwesomeIcon icon={faArrowLeft} /> Back to dashboard
        </Link>
      </div>
    )
  }

  const icon = resolveRoomIcon(room, categoryKey)
  const subtitle = room.isPrivate
    ? `${categoryName} · private · ${roomType}`
    : `${categoryName} · public · ${roomType}`

  const navTitle =
    roomType === 'Info' ? 'Info' : roomType === 'RoleClaim' ? 'Role Claim' : 'Chat'

  const canManageInfo = hasPermission(MANAGE_SERVER_INFRASTRUCTURE_BIT)

  return (
    <div className="chat-room-page chat-room-page--active">
      <ServerMaintenanceNav title={navTitle} />

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
        <CustomInfoRoomPanel roomId={decodedRoomId} canEdit={canManageInfo} />
      )}

      {roomType === 'RoleClaim' && <CustomRoleClaimPanel roomId={decodedRoomId} />}

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
