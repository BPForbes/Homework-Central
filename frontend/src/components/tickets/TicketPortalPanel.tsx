import { useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { infrastructureApi } from '../../api/infrastructureApi'
import { ticketsApi } from '../../api/ticketsApi'
import type { TicketAnswers, TicketPortalConfig } from '../../types/tickets'
import { TicketIntakeWizard } from './TicketIntakeWizard'
import { LoadingBars } from '../LoadingBars'

type TicketPortalPanelProps = {
  roomId: string
  onOpened: (ticketRoomId: string) => void
}

export function TicketPortalPanel({ roomId, onOpened }: TicketPortalPanelProps) {
  const [config, setConfig] = useState<TicketPortalConfig | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [wizardOpen, setWizardOpen] = useState(false)
  const [searchParams] = useSearchParams()

  const reportPrefill = useMemo(() => {
    const reportMessageId = searchParams.get('reportMessageId')
    if (!reportMessageId)
      return null
    return {
      reportMessageId,
      reportRoomId: searchParams.get('reportRoomId') ?? '',
      reportedUserId: searchParams.get('reportedUserId') ?? '',
      reportedUsername: searchParams.get('reportedUsername') ?? '',
      reportSnippet: searchParams.get('reportSnippet') ?? '',
    }
  }, [searchParams])

  useEffect(() => {
    let cancelled = false

    async function load() {
      setLoading(true)
      setError('')
      try {
        const channel = await infrastructureApi.getChannelByRoom(roomId)
        const { data } = await ticketsApi.getPortalConfig(channel.data.channelId)
        if (!cancelled) {
          setConfig(data)
          if (reportPrefill)
            setWizardOpen(true)
        }
      } catch {
        if (!cancelled) setError('Could not load this ticket portal.')
      } finally {
        if (!cancelled) setLoading(false)
      }
    }

    void load()
    return () => {
      cancelled = true
    }
  }, [roomId, reportPrefill])

  const initialAnswers: TicketAnswers = useMemo(() => {
    if (!config || !reportPrefill)
      return {}
    const answers: TicketAnswers = {}
    for (const question of config.intakeQuestions) {
      if (question.tracksUser && reportPrefill.reportedUserId)
        answers[question.id] = reportPrefill.reportedUserId
      if (question.type === 'mixed' || question.type === 'messageForward') {
        answers[question.id] = [{
          kind: 'forward',
          roomId: reportPrefill.reportRoomId,
          messageId: reportPrefill.reportMessageId,
          snippet: reportPrefill.reportSnippet,
          senderUsername: reportPrefill.reportedUsername,
        }]
      }
    }
    return answers
  }, [config, reportPrefill])

  if (loading) return <LoadingBars message="Loading ticket portal…" />
  if (error || !config) return <p className="chat-room-error">{error || 'Unavailable.'}</p>

  return (
    <div className="ticket-portal">
      <p className="ticket-portal-description">{config.description}</p>
      <button type="button" className="btn-primary ticket-portal-cta" onClick={() => setWizardOpen(true)}>
        {config.ctaLabel}
      </button>

      {wizardOpen && (
        <TicketIntakeWizard
          portalRoomId={roomId}
          questions={config.intakeQuestions}
          initialAnswers={initialAnswers}
          onCancel={() => setWizardOpen(false)}
          onOpened={(ticket) => {
            setWizardOpen(false)
            onOpened(ticket.roomId)
          }}
        />
      )}
    </div>
  )
}
