import { NavLink, useLocation } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import type { IconDefinition } from '@fortawesome/fontawesome-svg-core'
import { useAuth } from '../../context/AuthContext'
import { MANAGE_SERVER_INFRASTRUCTURE_BIT } from '../../constants/permissions'
import { byPrefixAndName } from '../../icons/byPrefixAndName'

function RailLink({ to, label, icon, end }: { to: string; label: string; icon: IconDefinition; end?: boolean }) {
  return (
    <NavLink
      to={to}
      end={end}
      className={({ isActive }) => (isActive ? 'app-rail-link app-rail-link--active' : 'app-rail-link')}
      title={label}
      aria-label={label}
    >
      <FontAwesomeIcon icon={icon} />
      <span className="app-rail-link-label">{label}</span>
    </NavLink>
  )
}

export function AppRail() {
  const { hasPermission } = useAuth()
  const location = useLocation()
  const canManageInfrastructure = hasPermission(MANAGE_SERVER_INFRASTRUCTURE_BIT)
  const onAdminRoute = location.pathname.startsWith('/user-config') || location.pathname.startsWith('/server')

  return (
    <nav className="app-rail" aria-label="Primary navigation">
      <RailLink to="/chat" label="Chat" icon={byPrefixAndName.far.comments} end={false} />
      <RailLink to="/inbox" label="Inbox" icon={byPrefixAndName.fas.envelope} />
      {canManageInfrastructure && (
        <>
          <RailLink to="/user-config" label="User Config" icon={byPrefixAndName.fas['users-gear']} end={false} />
          <RailLink to="/server" label="Server" icon={byPrefixAndName.fas.server} end={false} />
        </>
      )}
      <span className="app-rail-context" aria-hidden="true">
        {onAdminRoute ? 'Admin' : 'Chat'}
      </span>
    </nav>
  )
}

export function shouldShowChatSidebar(pathname: string): boolean {
  return pathname.startsWith('/chat')
    || pathname.startsWith('/inbox')
    || pathname === '/dashboard'
    || pathname.startsWith('/get-roles')
}
