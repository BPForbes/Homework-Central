import { useCallback, useEffect, useState } from 'react'
import { NavLink, useLocation } from 'react-router-dom'
import { ChevronDown, ChevronUp, MessageSquare, X } from 'lucide-react'
import { chatApi } from '../../api/chatApi'
import { useAuth } from '../../context/AuthContext'
import { useChatNavSync } from '../../hooks/useChatNavSync'
import { GET_ROLES_ROOM_ID } from '../../types/chat'
import type { ChatNav, ChatNavCategory } from '../../types/chat'
import { getCategoryIcon, getRoomIcon, getStaffRoomIcon } from './chatIcons'
import { resolveCustomRoomIcon } from '../infrastructure/customRoomIcons'
import { ChatRoomIcon } from './ChatRoomIcon'
import { cn } from '../../lib/utils'

interface ChatSidebarProps {
  variant?: 'persistent' | 'overlay'
  open?: boolean
  onClose?: () => void
}

export function ChatSidebar({ variant = 'persistent', open = true, onClose }: ChatSidebarProps) {
  const location = useLocation()
  const { user } = useAuth()
  const [nav, setNav] = useState<ChatNav | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [collapsed, setCollapsed] = useState<Set<string>>(new Set())

  const isOverlay = variant === 'overlay'
  const isOpen = isOverlay ? open : true

  const loadNav = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const { data } = await chatApi.getNav()
      setNav(data)
    } catch {
      setError('Could not load chat rooms.')
    } finally {
      setLoading(false)
    }
  }, [])

  useChatNavSync(() => {
    void loadNav()
  })

  useEffect(() => {
    void loadNav()
  }, [loadNav, user?.generalSubjectMask, user?.subjectExpertiseMasks, user?.roleMask, user?.permissionMask])

  useEffect(() => {
    if (isOverlay && open)
      void loadNav()
  }, [isOverlay, open, loadNav])

  useEffect(() => {
    if (!isOverlay)
      return
    document.body.style.overflow = open ? 'hidden' : ''
    return () => {
      document.body.style.overflow = ''
    }
  }, [isOverlay, open])

  function toggleGroup(key: string) {
    setCollapsed((prev) => {
      const next = new Set(prev)
      if (next.has(key))
        next.delete(key)
      else
        next.add(key)
      return next
    })
  }

  const sidebarContent = (
    <>
      <div className="flex items-center justify-between px-5 py-4 border-b border-border">
        <div className="flex items-center gap-2.5">
          <MessageSquare size={20} className="text-primary" />
          <span className="font-semibold text-foreground text-base">Chats</span>
        </div>
        {isOverlay && onClose && (
          <button
            type="button"
            onClick={onClose}
            className="w-7 h-7 rounded-lg flex items-center justify-center text-muted-foreground hover:bg-muted transition-colors"
            aria-label="Close"
          >
            <X size={14} />
          </button>
        )}
      </div>

      <div className="flex-1 overflow-y-auto py-3 px-3 space-y-2">
        {loading && <p className="text-sm text-muted-foreground px-3 py-2">Loading rooms…</p>}
        {error && <p className="text-sm text-destructive px-3 py-2">{error}</p>}
        {!loading && !error && nav?.categories.length === 0 && (
          <p className="text-sm text-muted-foreground px-3 py-2">
            No chat rooms available for your profile yet.
          </p>
        )}
        {nav?.categories.map((category) => (
          <CategorySection
            key={category.key}
            category={category}
            collapsed={collapsed.has(category.key)}
            onToggle={() => toggleGroup(category.key)}
            currentPath={location.pathname}
          />
        ))}
      </div>
    </>
  )

  if (isOverlay) {
    return (
      <>
        <button
          type="button"
          className={cn(
            'fixed inset-0 bg-slate-900/45 border-none z-[90] transition-opacity',
            isOpen ? 'opacity-100 pointer-events-auto' : 'opacity-0 pointer-events-none',
          )}
          aria-label="Close chat menu"
          onClick={onClose}
          tabIndex={isOpen ? undefined : -1}
        />
        <aside
          className={cn(
            'fixed top-0 left-0 w-72 max-w-[88vw] h-full bg-card border-r border-border z-[100] flex flex-col transition-transform duration-300',
            isOpen ? 'translate-x-0' : '-translate-x-full',
          )}
          aria-hidden={!isOpen}
        >
          {sidebarContent}
        </aside>
      </>
    )
  }

  return (
    <aside className="w-72 shrink-0 flex flex-col bg-card border-r border-border">
      {sidebarContent}
    </aside>
  )
}

function CategorySection({
  category,
  collapsed,
  onToggle,
  currentPath,
}: {
  category: ChatNavCategory
  collapsed: boolean
  onToggle: () => void
  currentPath: string
}) {
  const isStaff = category.key === 'Staff'
  const isGeneral = category.key === 'General'

  return (
    <div className="rounded-xl overflow-hidden border border-border/60 bg-muted/30">
      <button
        type="button"
        onClick={onToggle}
        className="w-full flex items-center justify-between px-4 py-3 hover:bg-muted/50 transition-colors"
      >
        <div className="flex items-center gap-2.5">
          <ChatRoomIcon
            icon={getCategoryIcon(category.key)}
            isPrivate={category.isPrivateCategory}
            className="text-primary w-4 h-4"
          />
          <span className="font-semibold text-sm text-foreground">{category.name}</span>
        </div>
        {collapsed
          ? <ChevronDown size={15} className="text-muted-foreground" />
          : <ChevronUp size={15} className="text-muted-foreground" />}
      </button>
      {!collapsed && (
        <div className="border-t border-border/40">
          {category.rooms.map((room) => {
            const baseIcon = room.iconName
              ? resolveCustomRoomIcon(room.iconName, room.roomType as 'Chat' | 'Info' | 'RoleClaim' | undefined)
              : room.roomType === 'Info'
                ? getCategoryIcon('General')
                : room.roomType === 'RoleClaim' || room.id === GET_ROLES_ROOM_ID
                  ? resolveCustomRoomIcon(null, 'RoleClaim')
                  : isStaff
                    ? getStaffRoomIcon(room.name)
                    : isGeneral
                      ? getCategoryIcon('General')
                      : getRoomIcon(room.name, category.key)

            const roomPath = `/chat/${encodeURIComponent(room.id)}`
            const isActive = currentPath === roomPath

            return (
              <NavLink
                key={room.id}
                to={roomPath}
                className={cn(
                  'w-full flex items-center gap-3 px-4 py-2.5 transition-colors text-left',
                  isActive
                    ? 'bg-secondary/70 text-primary'
                    : 'hover:bg-muted/60 text-foreground',
                )}
              >
                <ChatRoomIcon icon={baseIcon} isPrivate={room.isPrivate} className="text-primary w-[18px]" />
                <span className="text-sm font-medium truncate">{room.name}</span>
              </NavLink>
            )
          })}
        </div>
      )}
    </div>
  )
}
