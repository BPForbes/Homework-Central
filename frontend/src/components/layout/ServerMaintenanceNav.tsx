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

function NavIconLink({ to, label, icon, end }: { to: string; label: string; icon: IconDefinition; end?: boolean }) {
  return (
    <NavLink
      to={to}
      end={end}
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
        <NavIconLink to="/chat" label="Chat" icon={byPrefixAndName.far.comments} end={false} />
        <NavSeparator />
        <NavIconLink to="/inbox" label="Inbox" icon={byPrefixAndName.fas.envelope} />
        {canManageInfrastructure && (
          <>
            <NavSeparator />
            <NavIconLink
              to="/user-config"
              label="User Config"
              icon={byPrefixAndName.fas['users-gear']}
              end={false}
            />
            <NavSeparator />
            <NavLink
              to="/server"
              end={false}
              className={({ isActive }) =>
                isActive ? 'server-nav-link server-nav-link--active' : 'server-nav-link'
              }
              title="Server"
              aria-label="Server"
            >
              <FontAwesomeIcon icon={byPrefixAndName.fas.server} />
              <span className="server-nav-link-label">Server</span>
            </NavLink>
          </>
        )}
      </div>
    </nav>
  )
}
