import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faInbox } from '@fortawesome/free-solid-svg-icons'
import { inboxApi } from '../../api/inboxApi'
import { useAuth } from '../../context/AuthContext'
import { useChatNavSync } from '../../hooks/useChatNavSync'
import { INBOX_UPDATED_EVENT } from '../../utils/inboxEvents'
import { LoadingBars } from '../LoadingBars'
import { getCategoryIcon } from '../chat/chatIcons'
import type { ChatInboxSummary } from '../../types/inbox'

export function InboxSidebar() {
  const { user } = useAuth()
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const selectedCategoryKey = searchParams.get('category')
  const [summary, setSummary] = useState<ChatInboxSummary | null>(null)
  const [error, setError] = useState('')

  const loadSummary = useCallback(async () => {
    setError('')
    try {
      const { data } = await inboxApi.getSummary()
      setSummary(data)
    } catch {
      setError('Could not load inbox categories.')
    }
  }, [])

  useChatNavSync(() => {
    void loadSummary()
  })

  useEffect(() => {
    void loadSummary()
  }, [
    loadSummary,
    user?.generalSubjectMask,
    user?.subjectExpertiseMasks,
    user?.roleMask,
    user?.permissionMask,
  ])

  useEffect(() => {
    const refresh = () => void loadSummary()
    window.addEventListener(INBOX_UPDATED_EVENT, refresh)
    return () => window.removeEventListener(INBOX_UPDATED_EVENT, refresh)
  }, [loadSummary])

  useEffect(() => {
    if (
      summary
      && selectedCategoryKey
      && !summary.categories.some((category) => category.categoryKey === selectedCategoryKey)
    ) {
      navigate('/inbox', { replace: true })
    }
  }, [navigate, selectedCategoryKey, summary])

  const collectiveUnreadCount = useMemo(
    () => summary?.categories.reduce((total, category) => total + category.unreadCount, 0) ?? 0,
    [summary],
  )

  function linkClassName(isActive: boolean): string {
    return 'chat-room-link inbox-sidebar-link ' + (isActive ? 'active' : '')
  }

  return (
    <aside className="chat-sidebar inbox-sidebar" aria-label="Inbox categories">
      <div className="chat-sidebar-header">
        <h2>
          <FontAwesomeIcon icon={faInbox} className="chat-sidebar-title-icon" />
          Inbox
        </h2>
      </div>

      <div className="chat-sidebar-body">
        {!summary && !error && <LoadingBars message="Loading inbox…" />}
        {error && <p className="chat-sidebar-error">{error}</p>}

        {summary && (
          <section className="chat-category chat-category--public">
            <div className="chat-category-label-row">
              <span className="chat-category-label">Inbox views</span>
            </div>
            <ul className="chat-room-list">
              <li>
                <Link
                  to="/inbox"
                  className={linkClassName(!selectedCategoryKey)}
                  aria-current={!selectedCategoryKey ? 'page' : undefined}
                >
                  <FontAwesomeIcon icon={faInbox} className="chat-room-icon" />
                  <span className="chat-room-name">Collective</span>
                  <span
                    className="inbox-sidebar-count"
                    aria-label={collectiveUnreadCount + ' unread'}
                  >
                    {collectiveUnreadCount}
                  </span>
                </Link>
              </li>
              {summary.categories.map((category) => (
                <li key={category.categoryKey}>
                  <Link
                    to={'/inbox?category=' + encodeURIComponent(category.categoryKey)}
                    className={linkClassName(selectedCategoryKey === category.categoryKey)}
                    aria-current={selectedCategoryKey === category.categoryKey ? 'page' : undefined}
                  >
                    <FontAwesomeIcon
                      icon={getCategoryIcon(category.categoryKey)}
                      className="chat-room-icon"
                    />
                    <span className="chat-room-name">{category.categoryDisplayName}</span>
                    <span
                      className="inbox-sidebar-count"
                      aria-label={category.unreadCount + ' unread'}
                    >
                      {category.unreadCount}
                    </span>
                  </Link>
                </li>
              ))}
            </ul>
          </section>
        )}
      </div>
    </aside>
  )
}
