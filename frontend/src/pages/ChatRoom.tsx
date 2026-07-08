import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { ArrowLeft } from 'lucide-react'
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
import { getCategoryIcon, getRoomIcon, getStaffRoomIcon } from '../components/chat/chatIcons'
import { resolveCustomRoomIcon } from '../components/infrastructure/customRoomIcons'
import { cn } from '../lib/utils'

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
      <div className="flex-1 flex items-center justify-center text-muted-foreground text-sm">
        Loading…
      </div>
    )
  }

  if (!room) {
    return (
      <div className="flex-1 flex flex-col items-center justify-center gap-3 p-6">
        <p className="text-destructive text-sm">
          {accessDenied ? 'You do not have access to this room.' : 'This room is not available.'}
        </p>
        <Link to="/chat" className="inline-flex items-center gap-2 text-primary font-medium text-sm hover:underline">
          <ArrowLeft size={16} />
          Back to channels
        </Link>
      </div>
    )
  }

  const icon = resolveRoomIcon(room)
  const subtitle = room.isPrivate
    ? `${room.categoryDisplayName} · private · ${navTitleForRoomType(roomType)}`
    : `${room.categoryDisplayName} · public · ${navTitleForRoomType(roomType)}`

  const isRoleClaim = roomType === 'RoleClaim' || roomType === 'GetRoles'

  return (
    <div className="flex-1 flex flex-col min-w-0 min-h-0 bg-background">
      <header className="px-6 py-3 border-b border-border bg-card flex items-center gap-3 shrink-0">
        <Link
          to="/chat"
          className="w-8 h-8 rounded-full flex items-center justify-center text-muted-foreground hover:bg-muted transition-colors"
          aria-label="Back to channels"
        >
          <ArrowLeft size={16} />
        </Link>
        <div className="w-10 h-10 rounded-xl bg-primary flex items-center justify-center text-white shrink-0 text-base">
          <ChatRoomIcon icon={icon} isPrivate={room.isPrivate} className="text-white text-base" />
        </div>
        <div className="min-w-0">
          <div className="font-semibold text-foreground text-base truncate">{room.name}</div>
          <div className="text-xs text-muted-foreground truncate">{subtitle}</div>
        </div>
      </header>

      {roomType === 'Info' && (
        <div className="flex-1 overflow-y-auto p-6">
          <CustomInfoRoomPanel
            room={room}
            onUpdated={(content) => setRoom((prev) => (prev ? { ...prev, infoContent: content } : prev))}
          />
        </div>
      )}

      {isRoleClaim && (
        <div className={cn('flex-1 overflow-y-auto', isRoleClaim && 'px-8 py-7')}>
          {roomType === 'GetRoles' ? (
            <GetRolesPanel roomId={decodedRoomId} />
          ) : (
            <CustomRoleClaimPanel roomId={decodedRoomId} />
          )}
        </div>
      )}

      {isChatRoom && (
        <>
          {messagesError && (
            <p className="text-sm text-destructive px-6 py-2 bg-destructive/5 border-b border-destructive/20">
              {messagesError}
            </p>
          )}
          <div className="flex-1 flex flex-col min-h-0 relative">
            <ChatMessageList
              messages={messages}
              typingUsers={typingUsers}
              loading={messagesLoading}
              currentUserId={user?.userId}
              mentionRoles={mentionRoles}
              onReply={startReply}
            />
            {room.isPrivate && (
              <div className="absolute bottom-24 right-6 opacity-90 pointer-events-none" aria-hidden="true">
                <ChatRoomIcon icon={icon} isPrivate className="text-3xl" />
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
