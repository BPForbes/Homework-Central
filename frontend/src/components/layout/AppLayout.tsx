import { Outlet, useNavigate } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { useAuth } from '../../context/AuthContext'
import { ChatSidebar } from '../chat/ChatSidebar'
import { ThemeToggle } from '../ThemeToggle'
import { byPrefixAndName } from '../../icons/byPrefixAndName'

export function AppLayout() {
  const { user, logout } = useAuth()
  const navigate = useNavigate()

  async function handleLogout() {
    await logout()
    navigate('/login')
  }

  return (
    <div className="app-layout">
      <ChatSidebar />

      <div className="app-content">
        <header className="app-header">
          <div className="app-header-start">
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
    </div>
  )
}
