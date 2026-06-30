import { useEffect, useState } from 'react'
import { NavLink, useLocation } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faChevronDown, faChevronRight, faComments, faTimes } from '@fortawesome/free-solid-svg-icons'
import { chatApi } from '../../api/chatApi'
import type { ChatNav, ChatNavCategory } from '../../types/chat'
import { getCategoryIcon, getRoomIcon, getStaffRoomIcon } from './chatIcons'

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

  useEffect(() => {
    if (location.pathname.startsWith('/chat/'))
      onClose()
  }, [location.pathname, onClose])

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
      />
      <aside className={`chat-sidebar ${open ? 'open' : ''}`} aria-hidden={!open}>
        <div className="chat-sidebar-header">
          <h2>
            <FontAwesomeIcon icon={faComments} className="chat-sidebar-title-icon" />
            Chats
          </h2>
          <button type="button" className="chat-sidebar-close" onClick={onClose} aria-label="Close">
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
}: {
  category: ChatNavCategory
  expanded: boolean
  onToggle: () => void
}) {
  const isStaff = category.key === 'Staff'

  return (
    <section className="chat-category">
      <button type="button" className="chat-category-toggle" onClick={onToggle}>
        <span className="chat-category-label">
          <FontAwesomeIcon icon={getCategoryIcon(category.key)} className="chat-category-icon" />
          {category.name}
        </span>
        <FontAwesomeIcon icon={expanded ? faChevronDown : faChevronRight} className="chat-category-chevron" />
      </button>
      {expanded && (
        <ul className="chat-room-list">
          {category.rooms.map((room) => (
            <li key={room.id}>
              <NavLink
                to={`/chat/${encodeURIComponent(room.id)}`}
                className={({ isActive }) => `chat-room-link ${isActive ? 'active' : ''}`}
              >
                <FontAwesomeIcon
                  icon={isStaff ? getStaffRoomIcon(room.name) : getRoomIcon(room.name, category.key)}
                  className="chat-room-icon"
                />
                <span className="chat-room-name">{room.name}</span>
              </NavLink>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
