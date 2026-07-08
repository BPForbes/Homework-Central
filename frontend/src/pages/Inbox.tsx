import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faAt, faReply, faTrash } from '@fortawesome/free-solid-svg-icons'
import { inboxApi } from '../api/inboxApi'
import { MentionContent } from '../components/chat/MentionContent'
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
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set())
  const [deleting, setDeleting] = useState(false)

  useEffect(() => {
    void inboxApi
      .getInbox()
      .then(({ data }) => setItems(data))
      .catch(() => setError('Could not load your inbox.'))
      .finally(() => setLoading(false))
  }, [])

  const allSelected = items.length > 0 && selectedIds.size === items.length
  const someSelected = selectedIds.size > 0

  const selectedCountLabel = useMemo(() => {
    if (selectedIds.size === 0)
      return 'Delete selected'
    return `Delete selected (${selectedIds.size})`
  }, [selectedIds.size])

  function toggleSelect(notificationId: string) {
    setSelectedIds((prev) => {
      const next = new Set(prev)
      if (next.has(notificationId))
        next.delete(notificationId)
      else
        next.add(notificationId)
      return next
    })
  }

  function toggleSelectAll() {
    if (allSelected) {
      setSelectedIds(new Set())
      return
    }

    setSelectedIds(new Set(items.map((item) => item.notificationId)))
  }

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

  async function handleDeleteSelected() {
    if (!someSelected || deleting)
      return

    if (!window.confirm(`Delete ${selectedIds.size} selected inbox item${selectedIds.size === 1 ? '' : 's'}?`))
      return

    const ids = [...selectedIds]
    setDeleting(true)
    setError('')
    try {
      await inboxApi.deleteItems(ids)
      setItems((prev) => prev.filter((item) => !selectedIds.has(item.notificationId)))
      setSelectedIds(new Set())
    } catch {
      setError('Could not delete the selected inbox items.')
    } finally {
      setDeleting(false)
    }
  }

  async function handleClearAll() {
    if (items.length === 0 || deleting)
      return

    if (!window.confirm('Clear your entire inbox? This cannot be undone.'))
      return

    setDeleting(true)
    setError('')
    try {
      await inboxApi.deleteAll()
      setItems([])
      setSelectedIds(new Set())
    } catch {
      setError('Could not clear your inbox.')
    } finally {
      setDeleting(false)
    }
  }

  return (
    <div className="inbox-page">
      <header className="inbox-header">
        <div className="inbox-header-icon">
          <FontAwesomeIcon icon={byPrefixAndName.fas.envelope} />
        </div>
        <div>
          <h2>Inbox</h2>
          <p className="inbox-subtitle">Mentions and replies to your messages</p>
        </div>
      </header>

      {!loading && !error && items.length > 0 && (
        <div className="inbox-toolbar">
          <label className="inbox-select-all">
            <input
              type="checkbox"
              checked={allSelected}
              onChange={toggleSelectAll}
              aria-label="Select all inbox items"
            />
            <span>Select all</span>
          </label>
          <div className="inbox-toolbar-actions">
            <button
              type="button"
              className="inbox-toolbar-btn inbox-toolbar-btn--danger"
              disabled={!someSelected || deleting}
              onClick={() => void handleDeleteSelected()}
            >
              <FontAwesomeIcon icon={faTrash} />
              {selectedCountLabel}
            </button>
            <button
              type="button"
              className="inbox-toolbar-btn inbox-toolbar-btn--danger-outline"
              disabled={deleting}
              onClick={() => void handleClearAll()}
            >
              Clear all
            </button>
          </div>
        </div>
      )}

      {loading && <p className="inbox-status">Loading inbox…</p>}
      {error && <p className="inbox-error">{error}</p>}

      {!loading && !error && items.length === 0 && (
        <p className="inbox-empty">Nothing yet. @mentions and replies to your messages will show up here.</p>
      )}

      <ul className="inbox-list">
        {items.map((item) => {
          const isSelected = selectedIds.has(item.notificationId)

          return (
            <li
              key={item.notificationId}
              className={`inbox-item ${item.isRead ? 'inbox-item--read' : ''} ${isSelected ? 'inbox-item--selected' : ''}`}
            >
              <label className="inbox-item-checkbox" onClick={(event) => event.stopPropagation()}>
                <input
                  type="checkbox"
                  checked={isSelected}
                  onChange={() => toggleSelect(item.notificationId)}
                  aria-label={`Select inbox item from ${item.senderUsername}`}
                />
              </label>
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
          )
        })}
      </ul>
    </div>
  )
}
