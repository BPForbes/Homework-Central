import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faRotateLeft, faTrashCan, faXmark } from '@fortawesome/free-solid-svg-icons'
import { ticketsApi } from '../../api/ticketsApi'
import type { Ticket, TicketAnalyzeResult } from '../../types/tickets'
import { TicketIntakeSummary } from './TicketIntakeSummary'

type TicketChatChromeProps = {
  roomId: string
  /** Fired once the room is resolved as a ticket chat (true) or ordinary chat (false). */
  onTicketResolved?: (isTicketChat: boolean) => void
}

export function TicketChatChrome({ roomId, onTicketResolved }: TicketChatChromeProps) {
  const navigate = useNavigate()
  const [ticket, setTicket] = useState<Ticket | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [analysis, setAnalysis] = useState<TicketAnalyzeResult | null>(null)

  useEffect(() => {
    let cancelled = false
    setAnalysis(null)
    async function load() {
      try {
        const { data } = await ticketsApi.getByRoom(roomId)
        if (!cancelled) {
          setTicket(data)
          onTicketResolved?.(true)
        }
      } catch {
        if (!cancelled) {
          setTicket(null)
          onTicketResolved?.(false)
        }
      }
    }
    void load()
    return () => {
      cancelled = true
    }
  }, [roomId, onTicketResolved])

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

  async function analyze() {
    if (!ticket) return
    setBusy(true)
    setError('')
    try {
      const { data } = await ticketsApi.analyze(ticket.ticketId)
      setAnalysis(data)
      const refreshed = await ticketsApi.getByRoom(roomId)
      setTicket(refreshed.data)
    } catch {
      setError('AI assessment could not be completed.')
    } finally {
      setBusy(false)
    }
  }

  async function approveTraining(scoreEventId: string) {
    if (!ticket) return
    setBusy(true)
    setError('')
    try {
      const { data } = await ticketsApi.approveScoreTraining(ticket.ticketId, scoreEventId)
      setAnalysis((current) => current ? {
        ...current,
        messageScores: current.messageScores.map((score) =>
          score.scoreEventId === scoreEventId ? data : score),
      } : current)
    } catch {
      setError('That reviewer correction could not be approved for training.')
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

      {analysis && (
        <div className="ticket-watches">
          <h4>Hybrid confidence assessment</h4>
          {!analysis.available && (
            <p className="dashboard-hint">AI decisioning is opted out or currently unavailable.</p>
          )}
          {analysis.currentScore !== null && (
            <p className="dashboard-hint">
              Current confidence: <strong>{analysis.currentScore.toFixed(3)}</strong> / 1.000
            </p>
          )}
          {analysis.summary && <p className="dashboard-hint">{analysis.summary}</p>}
          {analysis.decision && (
            <p className="dashboard-hint">Suggested label: <strong>{analysis.decision}</strong></p>
          )}
          {analysis.messageScores.length > 0 && (
            <ul className="ticket-watches-list">
              {[...analysis.messageScores].reverse().slice(0, 10).map((score) => (
                <li key={score.scoreEventId} className="ticket-watch-chip">
                  <strong>
                    {score.scoreDelta >= 0 ? '+' : ''}{score.scoreDelta.toFixed(3)} → {score.currentScore.toFixed(3)}
                  </strong>
                  <span>
                    Student {score.studentScore.toFixed(3)} · confidence {score.studentConfidence.toFixed(3)} · {score.studentCategory}
                  </span>
                  {score.reviewerInvoked && score.reviewerScore !== null && (
                    <span>
                      Ollama reviewer {score.reviewerScore.toFixed(3)} · confidence {(score.reviewerConfidence ?? 0).toFixed(3)}
                      {score.correctionNeeded ? ' · correction suggested' : ' · validated'}
                    </span>
                  )}
                  <span>{score.reviewerExplanation || score.studentReasoning || score.reason}</span>
                  {score.reviewerGuidance && <small>Tutor guidance: {score.reviewerGuidance}</small>}
                  {score.trainingApprovedAtUtc && <small>Reviewer example approved for student training.</small>}
                  {ticket.canManage && score.reviewerScore !== null && !score.trainingApprovedAtUtc && (
                    <button
                      type="button"
                      className="btn-secondary"
                      disabled={busy}
                      onClick={() => void approveTraining(score.scoreEventId)}
                    >
                      Approve reviewer example for training
                    </button>
                  )}
                  <small>Message {score.messageId}</small>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

      {ticket.canManage && (
        <div className="ticket-chat-chrome-actions">
          {ticket.status === 'Open' ? (
          <>
          <button
            type="button"
            className="btn-primary"
            disabled={busy || ticket.aiTrackingOptOut}
            onClick={() => void analyze()}
          >
            Refresh AI assessment
          </button>
          {analysis?.decision && (
            <button
              type="button"
              className="btn-secondary"
              disabled={busy}
              onClick={() => void run(() => ticketsApi.approveDecision(
                ticket.ticketId,
                analysis.decision!,
                analysis.summary ?? undefined,
              ))}
            >
              Approve AI suggestion…
            </button>
          )}
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
