import type { TicketIntakeAnswer } from '../../types/tickets'

export function TicketIntakeSummary({ answers }: { answers: TicketIntakeAnswer[] }) {
  if (answers.length === 0) {
    return <p className="ticket-intake-summary-empty">No intake answers recorded.</p>
  }

  return (
    <dl className="ticket-intake-summary">
      {answers.map((answer) => (
        <div key={answer.questionId} className="ticket-intake-summary-row">
          <dt>{answer.prompt}</dt>
          <dd>{answer.valueDisplay}</dd>
        </div>
      ))}
    </dl>
  )
}
