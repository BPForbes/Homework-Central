import { useEffect, useRef, useState } from 'react'
import { NavLink, useLocation } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faChevronDown, faChevronRight, faComments, faTimes } from '@fortawesome/free-solid-svg-icons'
import { chatApi } from '../../api/chatApi'
import type { ChatNav, ChatNavCategory } from '../../types/chat'
import { getCategoryIcon, getRoomIcon, getStaffRoomIcon } from './chatIcons'
import { ChatRoomIcon } from './ChatRoomIcon'

interface ChatSidebarProps {
  open: boolean
  onClose: () => void
}

export function ChatSidebar({ open, onClose }: ChatSidebarProps) {
  const location = useLocation()
  const [nav, setNav] = useState<ChatNav | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [expanded, setExpanded] = useState<Record<string, boolean>>({})

  useEffect(() => {
    let cancelled = false

    const load = async () => {
      setLoading(true)
      setError(null)
      try {
        const { data } = await chatApi.getNav()
        if (!cancelled) {
          setNav(data)
          const initialExpanded: Record<string, boolean> = {}
          for (const category of data.categories)
            initialExpanded[category.key] = true
          setExpanded(initialExpanded)
        }
      } catch {
        if (!cancelled)
          setError('Could not load chat rooms.')
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

  const onCloseRef = useRef(onClose)
  onCloseRef.current = onClose

  useEffect(() => {
    if (location.pathname.startsWith('/chat/'))
      onCloseRef.current()
  }, [location.pathname])

  useEffect(() => {
    document.body.style.overflow = open ? 'hidden' : ''
    return () => {
      document.body.style.overflow = ''
    }
  }, [open])

  function toggleCategory(key: string) {
    setExpanded((prev) => ({ ...prev, [key]: !prev[key] }))
  }

  return (
    <>
      <button
        type="button"
        className={`chat-sidebar-backdrop ${open ? 'open' : ''}`}
        aria-label="Close chat menu"
        onClick={onClose}
        tabIndex={open ? undefined : -1}
      />
      <aside className={`chat-sidebar ${open ? 'open' : ''}`} aria-hidden={!open}>
        <div className="chat-sidebar-header">
          <h2>
            <FontAwesomeIcon icon={faComments} className="chat-sidebar-title-icon" />
            Chats
          </h2>
          <button
            type="button"
            className="chat-sidebar-close"
            onClick={onClose}
            aria-label="Close"
            tabIndex={open ? undefined : -1}
          >
            <FontAwesomeIcon icon={faTimes} />
          </button>
        </div>

        <div className="chat-sidebar-body">
          {loading && <p className="chat-sidebar-status">Loading rooms…</p>}
          {error && <p className="chat-sidebar-error">{error}</p>}
          {!loading && !error && nav?.categories.length === 0 && (
            <p className="chat-sidebar-status">No chat rooms available for your profile yet.</p>
          )}
          {nav?.categories.map((category) => (
            <CategorySection
              key={category.key}
              category={category}
              expanded={expanded[category.key] ?? true}
              onToggle={() => toggleCategory(category.key)}
              focusable={open}
            />
          ))}
        </div>
      </aside>
    </>
  )
}

function CategorySection({
  category,
  expanded,
  onToggle,
  focusable,
}: {
  category: ChatNavCategory
  expanded: boolean
  onToggle: () => void
  focusable: boolean
}) {
  const isStaff = category.key === 'Staff'
  const isGeneral = category.key === 'General'
  // The sidebar stays mounted while closed so its slide-in/out CSS transition can play, so its
  // buttons and links must be explicitly pulled out of the tab order (aria-hidden alone doesn't
  // do this in most browsers) rather than actually unmounted while off-screen.
  const tabIndex = focusable ? undefined : -1

  return (
    <section className={`chat-category ${category.isPrivateCategory ? 'chat-category--private' : 'chat-category--public'}`}>
      <button type="button" className="chat-category-toggle" onClick={onToggle} tabIndex={tabIndex}>
        <span className="chat-category-label">
          <FontAwesomeIcon icon={getCategoryIcon(category.key)} className="chat-category-icon" />
          {category.name}
        </span>
        <FontAwesomeIcon icon={expanded ? faChevronDown : faChevronRight} className="chat-category-chevron" />
      </button>
      {expanded && (
        <ul className="chat-room-list">
          {category.rooms.map((room) => {
            const baseIcon = isStaff
              ? getStaffRoomIcon(room.name)
              : isGeneral
                ? getCategoryIcon('General')
                : getRoomIcon(room.name, category.key)

            return (
            <li key={room.id}>
              <NavLink
                to={`/chat/${encodeURIComponent(room.id)}`}
                className={({ isActive }) => `chat-room-link ${isActive ? 'active' : ''}`}
                tabIndex={tabIndex}
              >
                <ChatRoomIcon
                  icon={baseIcon}
                  isPrivate={room.isPrivate}
                  className="chat-room-icon"
                />
                <span className="chat-room-name">{room.name}</span>
              </NavLink>
            </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}
