import { NavLink } from 'react-router-dom'
import { Inbox, MessageSquare, Server, Settings } from 'lucide-react'
import { useAuth } from '../../context/AuthContext'
import { MANAGE_SERVER_INFRASTRUCTURE_BIT } from '../../constants/permissions'
import { cn } from '../../lib/utils'
import { Button } from '../ui/button'

const TABS = [
  { to: '/chat', label: 'Chat', icon: MessageSquare, end: false },
  { to: '/inbox', label: 'Inbox', icon: Inbox, end: true },
] as const

export function AppTopNav({ onSignOut }: { onSignOut: () => void }) {
  const { user, hasPermission } = useAuth()
  const canManageInfrastructure = hasPermission(MANAGE_SERVER_INFRASTRUCTURE_BIT)

  return (
    <header className="h-12 bg-card border-b border-border flex items-center px-5 gap-1 shrink-0">
      <span className="font-semibold text-foreground mr-4">Homework Central</span>
      <div className="w-px h-5 bg-border mx-1" />
      {TABS.map((tab) => (
        <NavLink
          key={tab.to}
          to={tab.to}
          end={tab.end}
          className={({ isActive }) =>
            cn(
              'flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-sm font-medium transition-colors',
              isActive
                ? 'bg-secondary text-primary'
                : 'text-muted-foreground hover:text-foreground hover:bg-muted/60',
            )
          }
        >
          <tab.icon size={14} />
          {tab.label}
        </NavLink>
      ))}
      {canManageInfrastructure && (
        <>
          <NavLink
            to="/user-config"
            className={({ isActive }) =>
              cn(
                'flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-sm font-medium transition-colors',
                isActive
                  ? 'bg-secondary text-primary'
                  : 'text-muted-foreground hover:text-foreground hover:bg-muted/60',
              )
            }
          >
            <Settings size={14} />
            User Config
          </NavLink>
          <NavLink
            to="/server"
            className={({ isActive }) =>
              cn(
                'flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-sm font-medium transition-colors',
                isActive
                  ? 'bg-secondary text-primary'
                  : 'text-muted-foreground hover:text-foreground hover:bg-muted/60',
              )
            }
          >
            <Server size={14} />
            Server
          </NavLink>
        </>
      )}
      <div className="ml-auto flex items-center gap-3 text-sm text-muted-foreground">
        <span className="hidden sm:inline">
          {user?.username} ({user?.email})
        </span>
        <Button variant="outline" size="sm" onClick={onSignOut}>
          Sign out
        </Button>
      </div>
    </header>
  )
}
