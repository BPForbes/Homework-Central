import { useCallback, useEffect, useState } from 'react'
import { NavLink } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faComments, faIdBadge } from '@fortawesome/free-solid-svg-icons'
import { chatApi } from '../../api/chatApi'
import { useAuth } from '../../context/AuthContext'
import { useChatNavSync } from '../../hooks/useChatNavSync'
import { GET_ROLES_ROOM_ID } from '../../types/chat'
import type { ChatNav, ChatNavCategory } from '../../types/chat'
import { getCategoryIcon, getRoomIcon, getStaffRoomIcon } from './chatIcons'
import { resolveCustomRoomIcon } from '../infrastructure/customRoomIcons'
import { ChatRoomIcon } from './ChatRoomIcon'

export function ChatSidebar() {
  const { user } = useAuth()
  const [nav, setNav] = useState<ChatNav | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

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

  return (
    <aside className="chat-sidebar" aria-label="Chat rooms">
      <div className="chat-sidebar-header">
        <h2>
          <FontAwesomeIcon icon={faComments} className="chat-sidebar-title-icon" />
          Chats
        </h2>
      </div>

      <div className="chat-sidebar-body">
        {loading && <p className="chat-sidebar-status">Loading rooms…</p>}
        {error && <p className="chat-sidebar-error">{error}</p>}
        {!loading && !error && nav?.categories.length === 0 && (
          <p className="chat-sidebar-status">No chat rooms available for your profile yet.</p>
        )}
        {nav?.categories.map((category) => (
          <CategorySection key={category.key} category={category} />
        ))}
      </div>
    </aside>
  )
}

function CategorySection({ category }: { category: ChatNavCategory }) {
  const isStaff = category.key === 'Staff'
  const isGeneral = category.key === 'General'

  return (
    <section className={`chat-category ${category.isPrivateCategory ? 'chat-category--private' : 'chat-category--public'}`}>
      <div className="chat-category-label-row">
        <span className="chat-category-label">
          <FontAwesomeIcon icon={getCategoryIcon(category.key)} className="chat-category-icon" />
          {category.name}
        </span>
      </div>
      <ul className="chat-room-list">
        {category.rooms.map((room) => {
          const baseIcon = room.iconName
            ? resolveCustomRoomIcon(room.iconName, room.roomType as 'Chat' | 'Info' | 'RoleClaim' | undefined)
            : room.roomType === 'Info'
              ? getCategoryIcon('General')
              : room.roomType === 'RoleClaim' || room.id === GET_ROLES_ROOM_ID
                ? faIdBadge
                : isStaff
                  ? getStaffRoomIcon(room.name)
                  : isGeneral
                    ? getCategoryIcon('General')
                    : getRoomIcon(room.name, category.key)

          return (
            <li key={room.id}>
              <NavLink
                to={`/chat/${encodeURIComponent(room.id)}`}
                className={({ isActive }) => `chat-room-link ${isActive ? 'active' : ''}`}
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
    </section>
  )
}
