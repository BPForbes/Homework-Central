import { Link } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faTicket } from '@fortawesome/free-solid-svg-icons'
import type { ChatInboxItem } from '../../types/inbox'
import { formatUtcTimestamp } from '../../utils/formatUtcTimestamp'
import { TicketIntakeSummary } from '../tickets/TicketIntakeSummary'

type TicketInboxItemProps = {
  item: ChatInboxItem
  onOpen: (item: ChatInboxItem) => void
}

export function TicketInboxItem({ item, onOpen }: TicketInboxItemProps) {
  const isDecision = item.mentionKind === 'TicketDecision'

  return (
    <Link
      to={`/chat/${encodeURIComponent(item.roomId)}`}
      className="inbox-item-link inbox-item-link--ticket"
      onClick={() => onOpen(item)}
    >
      <div className="inbox-item-meta">
        <span className="inbox-item-category">{item.categoryDisplayName}</span>
        <time dateTime={item.createdAtUtc}>{formatUtcTimestamp(item.createdAtUtc)}</time>
      </div>
      <div className="inbox-item-room">#{item.roomDisplayName}</div>
      <div className="inbox-item-sender">
        <span className="inbox-item-kind inbox-item-kind--ticket">
          <FontAwesomeIcon icon={faTicket} /> Homework Central Automated System
        </span>
      </div>

      {isDecision ? (
        <>
          <div className="inbox-item-ticket-decision">{item.ticketDecision}</div>
          {item.ticketDecisionSummary && (
            <p className="inbox-item-message inbox-item-ticket-summary">{item.ticketDecisionSummary}</p>
          )}
        </>
      ) : (
        <TicketIntakeSummary answers={item.ticketIntakeAnswers ?? []} />
      )}

      <span className="inbox-item-cta">Open ticket</span>
    </Link>
  )
}
