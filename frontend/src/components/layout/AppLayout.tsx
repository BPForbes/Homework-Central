import { Outlet, useLocation, useNavigate } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { useAuth } from '../../context/useAuth'
import { ChatSidebar } from '../chat/ChatSidebar'
import { InboxSidebar } from '../inbox/InboxSidebar'
import { ThemeToggle } from '../ThemeToggle'
import { InfrastructureSidebar } from './InfrastructureSidebar'
import {
  shouldShowChatSidebar,
  shouldShowInboxSidebar,
  shouldShowInfrastructureSidebar,
} from './sidebarVisibility'
import { byPrefixAndName } from '../../icons/byPrefixAndName'

export function AppLayout() {
  const { user, logout } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const showChatSidebar = shouldShowChatSidebar(location.pathname)
  const showInboxSidebar = shouldShowInboxSidebar(location.pathname)
  const showInfrastructureSidebar = shouldShowInfrastructureSidebar(location.pathname)

  async function handleLogout() {
    await logout()
    navigate('/login')
  }

  return (
    <div className="app-layout">
      {showInfrastructureSidebar && <InfrastructureSidebar />}
      {showChatSidebar && <ChatSidebar />}
      {showInboxSidebar && <InboxSidebar />}

      <div className="app-content">
        <header className="app-header">
          <div className="app-header-start">
            <h1 className="app-title">
              <FontAwesomeIcon icon={byPrefixAndName.far.comments} className="app-title-icon" />
              <span className="app-title-text">Homework Central</span>
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
