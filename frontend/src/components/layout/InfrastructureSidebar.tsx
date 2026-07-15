import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import {
  faCircleInfo,
  faComments,
  faIdBadge,
  faLayerGroup,
  faMagnifyingGlass,
  faPlus,
  faServer,
  faShieldHalved,
  faUsersGear,
} from '@fortawesome/free-solid-svg-icons'
import { useLocation } from 'react-router-dom'
import {
  useServerNavSection,
  useUserConfigNavSection,
  type ServerNavSection,
  type UserConfigNavSection,
} from '../../hooks/useInfrastructureNav'

function SidebarSectionLink({
  active,
  label,
  icon,
  onClick,
}: {
  active: boolean
  label: string
  icon: typeof faComments
  onClick: () => void
}) {
  return (
    <li>
      <button
        type="button"
        className={`chat-room-link infra-sidebar-link ${active ? 'active' : ''}`}
        onClick={onClick}
      >
        <FontAwesomeIcon icon={icon} className="chat-room-icon" />
        <span className="chat-room-name">{label}</span>
      </button>
    </li>
  )
}

function ServerSidebar() {
  const [section, setSection] = useServerNavSection()

  const items: { id: ServerNavSection; label: string; icon: typeof faComments }[] = [
    { id: 'chat', label: 'Create Chat Room', icon: faComments },
    { id: 'roleclaim', label: 'Create Role Claim', icon: faIdBadge },
    { id: 'info', label: 'Create Info Page', icon: faCircleInfo },
    { id: 'rooms', label: 'All Rooms', icon: faLayerGroup },
  ]

  return (
    <aside className="chat-sidebar" aria-label="Server configuration">
      <div className="chat-sidebar-header">
        <h2>
          <FontAwesomeIcon icon={faServer} className="chat-sidebar-title-icon" />
          Server
        </h2>
      </div>
      <div className="chat-sidebar-body">
        <section className="chat-category chat-category--public">
          <div className="chat-category-label-row">
            <span className="chat-category-label">
              <FontAwesomeIcon icon={faLayerGroup} className="chat-category-icon" />
              Configuration
            </span>
          </div>
          <ul className="chat-room-list">
            {items.map((item) => (
              <SidebarSectionLink
                key={item.id}
                active={section === item.id}
                label={item.label}
                icon={item.icon}
                onClick={() => setSection(item.id)}
              />
            ))}
          </ul>
        </section>
      </div>
    </aside>
  )
}

function UserConfigSidebar() {
  const [section, setSection] = useUserConfigNavSection()

  const items: { id: UserConfigNavSection; label: string; icon: typeof faComments }[] = [
    { id: 'create', label: 'Create Roles', icon: faPlus },
    { id: 'manage', label: 'Manage Roles', icon: faUsersGear },
    { id: 'permissions', label: 'Roles & Permissions', icon: faShieldHalved },
    { id: 'users', label: 'User Search', icon: faMagnifyingGlass },
  ]

  return (
    <aside className="chat-sidebar" aria-label="User configuration">
      <div className="chat-sidebar-header">
        <h2>
          <FontAwesomeIcon icon={faUsersGear} className="chat-sidebar-title-icon" />
          User Config
        </h2>
      </div>
      <div className="chat-sidebar-body">
        <section className="chat-category chat-category--public">
          <div className="chat-category-label-row">
            <span className="chat-category-label">
              <FontAwesomeIcon icon={faUsersGear} className="chat-category-icon" />
              Configuration
            </span>
          </div>
          <ul className="chat-room-list">
            {items.map((item) => (
              <SidebarSectionLink
                key={item.id}
                active={section === item.id}
                label={item.label}
                icon={item.icon}
                onClick={() => setSection(item.id)}
              />
            ))}
          </ul>
        </section>
      </div>
    </aside>
  )
}

export function InfrastructureSidebar() {
  const { pathname } = useLocation()

  if (pathname.startsWith('/user-config'))
    return <UserConfigSidebar />
  if (pathname.startsWith('/server'))
    return <ServerSidebar />

  return null
}

export function shouldShowInfrastructureSidebar(pathname: string): boolean {
  return pathname.startsWith('/user-config') || pathname.startsWith('/server')
}

export function shouldShowChatSidebar(pathname: string): boolean {
  return pathname.startsWith('/chat')
    || pathname.startsWith('/inbox')
    || pathname === '/dashboard'
    || pathname.startsWith('/get-roles')
}
