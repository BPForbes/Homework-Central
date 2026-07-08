import { NavLink } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import type { IconDefinition } from '@fortawesome/fontawesome-svg-core'
import { useAuth } from '../../context/AuthContext'
import { MANAGE_SERVER_INFRASTRUCTURE_BIT } from '../../constants/permissions'
import { byPrefixAndName } from '../../icons/byPrefixAndName'

interface ServerMaintenanceNavProps {
  /** Page title shown before the nav links, e.g. "Chat" or "Server Maintenance". */
  title: string
}

function NavIconLink({ to, label, icon }: { to: string; label: string; icon: IconDefinition }) {
  return (
    <NavLink
      to={to}
      className={({ isActive }) =>
        isActive ? 'server-nav-link server-nav-link--active' : 'server-nav-link'
      }
      title={label}
      aria-label={label}
    >
      <FontAwesomeIcon icon={icon} />
      <span className="server-nav-link-label">{label}</span>
    </NavLink>
  )
}

function NavSeparator() {
  return (
    <span className="server-nav-separator" aria-hidden="true">
      |
    </span>
  )
}

export function ServerMaintenanceNav({ title }: ServerMaintenanceNavProps) {
  const { hasPermission } = useAuth()
  const canManageInfrastructure = hasPermission(MANAGE_SERVER_INFRASTRUCTURE_BIT)

  return (
    <nav className="server-maintenance-nav" aria-label="Server navigation">
      <div className="server-maintenance-nav-title">
        {title}
        <FontAwesomeIcon icon={byPrefixAndName.fas.server} className="server-maintenance-nav-title-icon" />
      </div>
      <div className="server-maintenance-nav-links">
        <NavIconLink to="/chat" label="Chat" icon={byPrefixAndName.far.comments} />
        <NavSeparator />
        <NavIconLink to="/inbox" label="Inbox" icon={byPrefixAndName.fas.envelope} />
        {canManageInfrastructure && (
          <>
            <NavSeparator />
            <NavIconLink
              to="/user-config"
              label="User Config"
              icon={byPrefixAndName.fas['users-gear']}
            />
            <NavSeparator />
            <NavIconLink to="/server" label="Server" icon={byPrefixAndName.fas.server} />
          </>
        )}
      </div>
    </nav>
  )
}
