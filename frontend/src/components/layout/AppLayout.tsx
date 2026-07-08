import { useCallback } from 'react'
import { Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../../context/AuthContext'
import { ChatSidebar } from '../chat/ChatSidebar'
import { AppTopNav } from './AppTopNav'
import { cn } from '../../lib/utils'

export function AppLayout() {
  const { logout } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const isChatRoute = location.pathname.startsWith('/chat')

  const handleLogout = useCallback(async () => {
    await logout()
    navigate('/login')
  }, [logout, navigate])

  return (
    <div className="h-screen w-screen flex flex-col overflow-hidden">
      <AppTopNav onSignOut={() => void handleLogout()} />

      <div className="flex-1 flex min-h-0">
        {isChatRoute && <ChatSidebar variant="persistent" />}
        <main
          className={cn(
            'flex-1 flex flex-col min-h-0 min-w-0 overflow-auto',
            !isChatRoute && 'p-6',
          )}
        >
          <Outlet />
        </main>
      </div>
    </div>
  )
}
