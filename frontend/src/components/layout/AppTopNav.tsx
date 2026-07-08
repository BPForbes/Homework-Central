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

  const navTabClass = (isActive: boolean) =>
    cn(
      'flex items-center gap-2 px-3 py-2 rounded-md text-sm font-medium transition-colors shrink-0 whitespace-nowrap',
      isActive
        ? 'bg-secondary text-primary'
        : 'text-muted-foreground hover:text-foreground hover:bg-muted/60',
    )

  return (
    <header className="h-12 bg-card border-b border-border flex items-center justify-between px-5 shrink-0 w-full">
      <div className="flex items-center gap-1 min-w-0">
        <span className="font-semibold text-foreground mr-3 shrink-0">Homework Central</span>
        <div className="w-px h-5 bg-border mx-1 shrink-0" />
        {TABS.map((tab) => (
          <NavLink
            key={tab.to}
            to={tab.to}
            end={tab.end}
            className={({ isActive }) => navTabClass(isActive)}
          >
            <tab.icon size={16} className="shrink-0" />
            {tab.label}
          </NavLink>
        ))}
        {canManageInfrastructure && (
          <>
            <NavLink
              to="/user-config"
              className={({ isActive }) => navTabClass(isActive)}
            >
              <Settings size={16} className="shrink-0" />
              User Config
            </NavLink>
            <NavLink
              to="/server"
              className={({ isActive }) => navTabClass(isActive)}
            >
              <Server size={16} className="shrink-0" />
              Server
            </NavLink>
          </>
        )}
      </div>
      <div className="flex items-center gap-3 text-sm text-muted-foreground shrink-0 ml-4">
        <span className="whitespace-nowrap">
          {user?.username} ({user?.email})
        </span>
        <Button variant="outline" size="sm" onClick={onSignOut}>
          Sign out
        </Button>
      </div>
    </header>
  )
}
