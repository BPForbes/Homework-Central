import { useEffect, useState } from 'react'
import { infrastructureApi } from '../../api/infrastructureApi'
import { ticketsApi } from '../../api/ticketsApi'
import type { TicketPortalConfig } from '../../types/tickets'
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

  useEffect(() => {
    let cancelled = false

    async function load() {
      setLoading(true)
      setError('')
      try {
        const channel = await infrastructureApi.getChannelByRoom(roomId)
        const { data } = await ticketsApi.getPortalConfig(channel.data.channelId)
        if (!cancelled) setConfig(data)
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
  }, [roomId])

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
