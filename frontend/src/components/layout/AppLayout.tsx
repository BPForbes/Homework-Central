import { useCallback, useState } from 'react'
import { Outlet, useNavigate } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faBars } from '@fortawesome/free-solid-svg-icons'
import { useAuth } from '../../context/AuthContext'
import { ChatSidebar } from '../chat/ChatSidebar'
import { ThemeToggle } from '../ThemeToggle'
import { byPrefixAndName } from '../../icons/byPrefixAndName'

export function AppLayout() {
  const { user, logout } = useAuth()
  const navigate = useNavigate()
  const [sidebarOpen, setSidebarOpen] = useState(false)
  const closeSidebar = useCallback(() => setSidebarOpen(false), [])

  async function handleLogout() {
    await logout()
    navigate('/login')
  }

  return (
    <div className="app-layout">
      <ChatSidebar open={sidebarOpen} onClose={closeSidebar} />

      <header className="app-header">
        <div className="app-header-start">
          <button
            type="button"
            className="chat-menu-toggle"
            onClick={() => setSidebarOpen(true)}
            aria-label="Open chats"
          >
            <FontAwesomeIcon icon={faBars} />
            <span>Chats</span>
          </button>
          <h1 className="app-title">
            <FontAwesomeIcon icon={byPrefixAndName.far.comments} className="app-title-icon" />
            Homework Central
          </h1>
        </div>
        <div className="user-info">
          <ThemeToggle />
          <span>
            {user?.username} ({user?.email})
          </span>
          <button type="button" onClick={handleLogout} className="btn-secondary">
            Sign out
          </button>
        </div>
      </header>

      <main className="app-main">
        <Outlet />
      </main>
    </div>
  )
}
