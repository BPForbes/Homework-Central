import { FormEvent, useEffect, useState } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faTrash } from '@fortawesome/free-solid-svg-icons'
import { aiTrackingApi } from '../../api/aiTrackingApi'
import type { AIModelLineage, AITrackingSession } from '../../types/aiTracking'

export function AITrackingDataPanel() {
  const [lineages, setLineages] = useState<AIModelLineage[]>([])
  const [sessions, setSessions] = useState<AITrackingSession[]>([])
  const [lineageSlug, setLineageSlug] = useState('')
  const [ticketId, setTicketId] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState('')
  const [customSlug, setCustomSlug] = useState('')
  const [customName, setCustomName] = useState('')
  const [customCategories, setCustomCategories] = useState('general')

  async function refresh(nextLineage = lineageSlug, nextTicket = ticketId) {
    const [lineageResponse, sessionResponse] = await Promise.all([
      aiTrackingApi.listLineages(),
      aiTrackingApi.querySessions({
        lineageSlug: nextLineage || undefined,
        ticketId: nextTicket || undefined,
        limit: 20,
      }),
    ])
    setLineages(lineageResponse.data)
    setSessions(sessionResponse.data.items)
  }

  useEffect(() => {
    void (async () => {
      try {
        const [lineageResponse, sessionResponse] = await Promise.all([
          aiTrackingApi.listLineages(),
          aiTrackingApi.querySessions({ limit: 20 }),
        ])
        setLineages(lineageResponse.data)
        setSessions(sessionResponse.data.items)
      } catch {
        setError('Could not load AI tracking data.')
      }
    })()
  }, [])

  async function onQuery(event: FormEvent) {
    event.preventDefault()
    setError('')
    setBusy('query')
    try {
      await refresh()
    } catch {
      setError('Could not query AI tracking sessions.')
    } finally {
      setBusy('')
    }
  }

  async function onDeleteSession(sessionId: number) {
    setBusy(`session-${sessionId}`)
    setError('')
    try {
      await aiTrackingApi.deleteSession(sessionId)
      await refresh()
    } catch {
      setError('Could not delete that tracking session.')
    } finally {
      setBusy('')
    }
  }

  async function onPurgeLineage(slug: string) {
    setBusy(`purge-${slug}`)
    setError('')
    try {
      await aiTrackingApi.deleteLineageSessions(slug)
      await refresh()
    } catch {
      setError('Could not purge sessions for that lineage.')
    } finally {
      setBusy('')
    }
  }

  async function onRegister(event: FormEvent) {
    event.preventDefault()
    const categories = customCategories
      .split(',')
      .map((entry) => entry.trim())
      .filter((entry) => entry.length > 0)
      .map((slug, index, all) => ({
        slug,
        displayName: slug,
        isCatchAll: index === all.length - 1,
      }))
    if (!customSlug.trim() || categories.length === 0) {
      setError('A custom lineage needs a slug and at least one category.')
      return
    }
    setBusy('register')
    setError('')
    try {
      await aiTrackingApi.registerLineage({
        slug: customSlug.trim(),
        displayName: customName.trim() || customSlug.trim(),
        categories,
      })
      setCustomSlug('')
      setCustomName('')
      setCustomCategories('general')
      await refresh()
    } catch {
      setError('Could not register that custom lineage.')
    } finally {
      setBusy('')
    }
  }

  return (
    <section className="sm-panel">
      <div className="sm-panel-header">
        <h3>AI tracking catalog</h3>
      </div>
      <p className="dashboard-hint">
        Lineages are lookup rows (built-in moderation/tutoring plus any custom ticket ANI).
        Sessions are entity rows; category weights are the junction to the category lookup.
      </p>
      {error && <p className="error">{error}</p>}
      <ul className="ticket-watches-list">
        {lineages.map((lineage) => (
          <li key={lineage.lineageId} className="ticket-watch-chip">
            <div className="ticket-watch-chip-header">
              <strong>{lineage.displayName} · {lineage.slug}</strong>
              <button
                type="button"
                className="btn-secondary"
                disabled={busy === `purge-${lineage.slug}` || lineage.sessionCount === 0}
                onClick={() => void onPurgeLineage(lineage.slug)}
              >
                Purge {lineage.sessionCount} sessions
              </button>
            </div>
            <span>
              {lineage.isBuiltIn ? 'Built-in' : 'Custom'}
              {lineage.portalChannelId ? ' · bound to a ticket portal' : ''}
              {' · '}
              {lineage.categoryCount} categories
            </span>
          </li>
        ))}
      </ul>
      <form className="sm-form-actions" onSubmit={(event) => void onQuery(event)}>
        <input
          className="sm-input"
          placeholder="Lineage slug"
          value={lineageSlug}
          onChange={(event) => setLineageSlug(event.target.value)}
        />
        <input
          className="sm-input"
          placeholder="Ticket id"
          value={ticketId}
          onChange={(event) => setTicketId(event.target.value)}
        />
        <button type="submit" className="btn-secondary" disabled={busy === 'query'}>Query sessions</button>
      </form>
      <ul className="ticket-watches-list">
        {sessions.map((session) => (
          <li key={session.sessionId} className="ticket-watch-chip">
            <div className="ticket-watch-chip-header">
              <strong>{session.lineageSlug} · message {session.messageIndex}</strong>
              <button
                type="button"
                className="ticket-watch-chip-remove"
                aria-label="Delete tracking session"
                disabled={busy === `session-${session.sessionId}`}
                onClick={() => void onDeleteSession(session.sessionId)}
              >
                <FontAwesomeIcon icon={faTrash} />
              </button>
            </div>
            <span>
              {session.ticketId ?? 'no ticket'}
              {' · '}
              {session.modelVersion}
              {' · '}
              {session.categoryWeights.map((weight) => `${weight.categorySlug} ${weight.weight.toFixed(2)}`).join(', ') || 'no weights'}
            </span>
          </li>
        ))}
      </ul>
      <form onSubmit={(event) => void onRegister(event)}>
        <p className="dashboard-hint">Register a custom lineage for a future ticket portal (comma-separated category slugs; last is the catch-all).</p>
        <div className="sm-form-actions">
          <input className="sm-input" placeholder="Slug" value={customSlug} onChange={(event) => setCustomSlug(event.target.value)} />
          <input className="sm-input" placeholder="Display name" value={customName} onChange={(event) => setCustomName(event.target.value)} />
          <input className="sm-input" placeholder="Categories" value={customCategories} onChange={(event) => setCustomCategories(event.target.value)} />
          <button type="submit" className="btn-primary" disabled={busy === 'register'}>Register lineage</button>
        </div>
      </form>
    </section>
  )
}
