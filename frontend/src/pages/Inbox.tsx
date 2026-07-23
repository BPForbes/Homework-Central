import { useEffect, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faAt, faReply, faTrash } from '@fortawesome/free-solid-svg-icons'
import { inboxApi } from '../api/inboxApi'
import { RichContent } from '../richtext/RichContent'
import { byPrefixAndName } from '../icons/byPrefixAndName'
import { ServerMaintenanceNav } from '../components/layout/ServerMaintenanceNav'
import { LoadingBars } from '../components/LoadingBars'
import { TicketInboxItem } from '../components/inbox/TicketInboxItem'
import { notifyInboxUpdated } from '../utils/inboxEvents'
import { formatUtcTimestamp } from '../utils/formatUtcTimestamp'
import type { ChatInboxItem } from '../types/inbox'

export function Inbox() {
  const [searchParams] = useSearchParams()
  const selectedCategoryKey = searchParams.get('category') || null
  const [items, setItems] = useState<ChatInboxItem[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set())
  const [deleting, setDeleting] = useState(false)

  useEffect(() => {
    let active = true
    setLoading(true)
    setError('')
    setItems([])
    setSelectedIds(new Set())

    void inboxApi
      .getInbox(selectedCategoryKey)
      .then(({ data }) => {
        if (!active)
          return

        // Keep the view correct during a rolling backend restart too. The
        // server remains the authorization boundary once the request lands.
        setItems(
          selectedCategoryKey
            ? data.filter((item) => item.categoryKey === selectedCategoryKey)
            : data,
        )
      })
      .catch(() => {
        if (active)
          setError('Could not load your inbox.')
      })
      .finally(() => {
        if (active)
          setLoading(false)
      })

    return () => {
      active = false
    }
  }, [selectedCategoryKey])

  const allSelected = items.length > 0 && selectedIds.size === items.length
  const someSelected = selectedIds.size > 0
  const selectedCategoryDisplayName = selectedCategoryKey
    ? items.find((item) => item.categoryKey === selectedCategoryKey)?.categoryDisplayName
    : null

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
        notifyInboxUpdated()
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
      notifyInboxUpdated()
    } catch {
      setError('Could not delete the selected inbox items.')
    } finally {
      setDeleting(false)
    }
  }

  async function handleClearAll() {
    if (items.length === 0 || deleting)
      return

    const confirmationMessage = selectedCategoryKey
      ? 'Clear every item in this category? This cannot be undone.'
      : 'Clear your entire inbox? This cannot be undone.'

    if (!window.confirm(confirmationMessage))
      return

    setDeleting(true)
    setError('')
    try {
      await inboxApi.deleteAll(selectedCategoryKey)

      setItems([])
      setSelectedIds(new Set())
      notifyInboxUpdated()
    } catch {
      setError('Could not clear your inbox.')
    } finally {
      setDeleting(false)
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
          <p className="inbox-subtitle">
            {selectedCategoryKey
              ? selectedCategoryDisplayName
                ? 'Mentions and replies in ' + selectedCategoryDisplayName
                : 'Mentions and replies in the selected category'
              : 'Mentions and replies to your messages'}
          </p>
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
              {selectedCategoryKey ? 'Clear category' : 'Clear all'}
            </button>
          </div>
        </div>
      )}

      {loading && <LoadingBars message="Loading inbox…" />}
      {error && <p className="inbox-error">{error}</p>}

      {!loading && !error && items.length === 0 && (
        <p className="inbox-empty">
          {selectedCategoryKey
            ? 'Nothing yet in this category.'
            : 'Nothing yet. @mentions and replies to your messages will show up here.'}
        </p>
      )}

      <ul className="inbox-list">
        {items.map((item) => {
          const isSelected = selectedIds.has(item.notificationId)
          const isTicketItem = item.mentionKind === 'Ticket' || item.mentionKind === 'TicketDecision'

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
              {isTicketItem ? (
                <TicketInboxItem item={item} onOpen={(entry) => void handleOpen(entry)} />
              ) : (
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
                    <RichContent content={item.messageContent} />
                  </div>
                </Link>
              )}
            </li>
          )
        })}
      </ul>
    </div>
  )
}
