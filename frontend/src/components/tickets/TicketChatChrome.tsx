import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faRotateLeft, faTrashCan, faXmark } from '@fortawesome/free-solid-svg-icons'
import { ticketsApi } from '../../api/ticketsApi'
import type { Ticket } from '../../types/tickets'
import { TicketIntakeSummary } from './TicketIntakeSummary'

type TicketChatChromeProps = {
  roomId: string
}

export function TicketChatChrome({ roomId }: TicketChatChromeProps) {
  const navigate = useNavigate()
  const [ticket, setTicket] = useState<Ticket | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    let cancelled = false
    async function load() {
      try {
        const { data } = await ticketsApi.getByRoom(roomId)
        if (!cancelled) setTicket(data)
      } catch {
        if (!cancelled) setTicket(null)
      }
    }
    void load()
    return () => {
      cancelled = true
    }
  }, [roomId])

  if (!ticket) return null

  async function run(action: () => Promise<{ data: Ticket } | void>) {
    setBusy(true)
    setError('')
    try {
      const result = await action()
      if (result && 'data' in result) {
        setTicket(result.data)
      } else {
        navigate('/dashboard')
      }
    } catch {
      setError('That ticket action could not be completed.')
    } finally {
      setBusy(false)
    }
  }

  const activeWatches = ticket.watches.filter((watch) => watch.isActive)

  return (
    <div className="ticket-chat-chrome">
      <div className="ticket-chat-chrome-meta">
        <span className={`ticket-status-badge ticket-status-badge--${ticket.status.toLowerCase()}`}>
          {ticket.status}
        </span>
        <span className="ticket-chat-chrome-opener">
          Opened by {ticket.openedByUsername}
        </span>
      </div>

      {ticket.intakeAnswers.length > 0 && (
        <div className="ticket-chat-chrome-intake">
          <h4>Intake</h4>
          <TicketIntakeSummary answers={ticket.intakeAnswers} />
        </div>
      )}

      {activeWatches.length > 0 && (
        <div className="ticket-watches">
          <h4>Watched users</h4>
          <ul className="ticket-watches-list">
            {activeWatches.map((watch) => (
              <li key={watch.watchId} className="ticket-watch-chip">
                <strong>{watch.trackedUsername}</strong>
                <span>{watch.contextLabel}</span>
                <small>{watch.source}</small>
              </li>
            ))}
          </ul>
        </div>
      )}

      {error && <p className="error">{error}</p>}

      {ticket.aiTrackingOptOut && (
        <p className="dashboard-hint">AI tracking opted out for this ticket.</p>
      )}
      {ticket.approvedDecision && (
        <p className="dashboard-hint">Approved decision: {ticket.approvedDecision}</p>
      )}

      {ticket.canManage && (
        <div className="ticket-chat-chrome-actions">
          {ticket.status === 'Open' ? (
          <>
          <button
            type="button"
            className="btn-primary"
            disabled={busy}
            onClick={() => void run(() => ticketsApi.analyze(ticket.ticketId).then(async (result) => {
              if (result.data.decision) {
                await ticketsApi.approveDecision(ticket.ticketId, result.data.decision, result.data.summary ?? undefined)
              }
              return ticketsApi.getByRoom(roomId)
            }))}
          >
            Analyze &amp; approve suggestion
          </button>
          <button
            type="button"
            className="btn-secondary"
            disabled={busy}
            onClick={() => {
              const decision = window.prompt('Decision label to approve (e.g. Approve)')
              if (!decision) return
              void run(() => ticketsApi.approveDecision(ticket.ticketId, decision))
            }}
          >
            Approve decision…
          </button>
          <button
            type="button"
            className="btn-secondary"
            disabled={busy}
            onClick={() => void run(() => ticketsApi.close(ticket.ticketId))}
          >
            <FontAwesomeIcon icon={faXmark} /> Close ticket
          </button>
          </>
          ) : (
            <>
              <button
                type="button"
                className="btn-primary"
                disabled={busy}
                onClick={() => void run(() => ticketsApi.reopen(ticket.ticketId))}
              >
                <FontAwesomeIcon icon={faRotateLeft} /> Reopen
              </button>
              <button
                type="button"
                className="btn-secondary ticket-chat-chrome-delete"
                disabled={busy}
                onClick={() => {
                  if (!window.confirm('Delete this closed ticket permanently?')) return
                  void run(() => ticketsApi.remove(ticket.ticketId))
                }}
              >
                <FontAwesomeIcon icon={faTrashCan} /> Delete
              </button>
            </>
          )}
        </div>
      )}
    </div>
  )
}
