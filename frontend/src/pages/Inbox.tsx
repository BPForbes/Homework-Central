import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faAt, faReply } from '@fortawesome/free-solid-svg-icons'
import { inboxApi } from '../api/inboxApi'
import { MentionContent } from '../components/chat/MentionContent'
import { ServerMaintenanceNav } from '../components/layout/ServerMaintenanceNav'
import { byPrefixAndName } from '../icons/byPrefixAndName'
import type { ChatInboxItem } from '../types/inbox'

function formatUtcTimestamp(iso: string): string {
  const date = new Date(iso)
  const year = date.getUTCFullYear()
  const month = String(date.getUTCMonth() + 1).padStart(2, '0')
  const day = String(date.getUTCDate()).padStart(2, '0')
  const hours = String(date.getUTCHours()).padStart(2, '0')
  const minutes = String(date.getUTCMinutes()).padStart(2, '0')
  return `${year}-${month}-${day} ${hours}:${minutes} UTC`
}

export function Inbox() {
  const [items, setItems] = useState<ChatInboxItem[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  useEffect(() => {
    void inboxApi
      .getInbox()
      .then(({ data }) => setItems(data))
      .catch(() => setError('Could not load your inbox.'))
      .finally(() => setLoading(false))
  }, [])

  async function handleOpen(item: ChatInboxItem) {
    if (!item.isRead) {
      try {
        await inboxApi.markRead(item.notificationId)
        setItems((prev) =>
          prev.map((entry) =>
            entry.notificationId === item.notificationId
              ? { ...entry, isRead: true, readAtUtc: new Date().toISOString() }
              : entry
          )
        )
      } catch {
        // Navigation still works if mark-read fails.
      }
    }
  }

  return (
    <div className="inbox-page">
      <ServerMaintenanceNav title="Inbox" />

      <header className="inbox-header">
        <div className="inbox-header-icon">
          <FontAwesomeIcon icon={byPrefixAndName.fas.envelope} />
        </div>
        <div>
          <h2>Inbox</h2>
          <p className="inbox-subtitle">Mentions and replies to your messages</p>
        </div>
      </header>

      {loading && <p className="inbox-status">Loading inbox…</p>}
      {error && <p className="inbox-error">{error}</p>}

      {!loading && !error && items.length === 0 && (
        <p className="inbox-empty">Nothing yet. @mentions and replies to your messages will show up here.</p>
      )}

      <ul className="inbox-list">
        {items.map((item) => (
          <li key={item.notificationId} className={item.isRead ? 'inbox-item inbox-item--read' : 'inbox-item'}>
            <Link
              to={`/chat/${encodeURIComponent(item.roomId)}`}
              className="inbox-item-link"
              onClick={() => void handleOpen(item)}
            >
              <div className="inbox-item-meta">
                <span className="inbox-item-category">{item.categoryDisplayName}</span>
                <time dateTime={item.createdAtUtc}>{formatUtcTimestamp(item.createdAtUtc)}</time>
              </div>
              <div className="inbox-item-room">#{item.roomDisplayName}</div>
              <div className="inbox-item-sender">
                {item.mentionKind === 'Reply' ? (
                  <span className="inbox-item-kind inbox-item-kind--reply">
                    <FontAwesomeIcon icon={faReply} /> {item.senderUsername} replied to you
                  </span>
                ) : (
                  <span className="inbox-item-kind inbox-item-kind--mention">
                    <FontAwesomeIcon icon={faAt} /> From {item.senderUsername}
                  </span>
                )}
              </div>
              <div className="inbox-item-message">
                <MentionContent content={item.messageContent} />
              </div>
            </Link>
          </li>
        ))}
      </ul>
    </div>
  )
}
