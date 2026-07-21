import { useState } from 'react'
import { ticketsApi } from '../../api/ticketsApi'
import { chatApi } from '../../api/chatApi'
import type { Ticket, TicketAnswers, TicketIntakeQuestion } from '../../types/tickets'

type TicketIntakeWizardProps = {
  portalRoomId: string
  questions: TicketIntakeQuestion[]
  initialAnswers?: TicketAnswers
  onCancel: () => void
  onOpened: (ticket: Ticket) => void
}

function needsOptions(type: TicketIntakeQuestion['type']): boolean {
  return type === 'multipleChoice' || type === 'multiSelect' || type === 'dropdown'
}

function validateAnswers(questions: TicketIntakeQuestion[], answers: TicketAnswers): string | null {
  for (const question of questions) {
    if (!question.required) continue
    const value = answers[question.id]
    if (value === null || value === undefined || value === '') {
      return `“${question.prompt}” is required.`
    }
    if (Array.isArray(value) && value.length === 0) {
      return `“${question.prompt}” needs at least one selection.`
    }
  }
  return null
}

function QuestionField({
  question,
  value,
  onChange,
}: {
  question: TicketIntakeQuestion
  value: TicketAnswers[string]
  onChange: (next: TicketAnswers[string]) => void
}) {
  const options = question.options ?? []

  switch (question.type) {
    case 'shortText':
      return (
        <input
          id={`ticket-q-${question.id}`}
          type="text"
          className="sm-input"
          value={typeof value === 'string' ? value : ''}
          onChange={(event) => onChange(event.target.value)}
          required={question.required}
        />
      )
    case 'longText':
      return (
        <textarea
          id={`ticket-q-${question.id}`}
          className="sm-input ticket-intake-textarea"
          rows={4}
          value={typeof value === 'string' ? value : ''}
          onChange={(event) => onChange(event.target.value)}
          required={question.required}
        />
      )
    case 'multipleChoice':
      return (
        <div className="ticket-intake-options" role="radiogroup" aria-labelledby={`ticket-q-label-${question.id}`}>
          {options.map((option) => (
            <label key={option} className="ticket-intake-option">
              <input
                type="radio"
                name={`ticket-q-${question.id}`}
                checked={value === option}
                onChange={() => onChange(option)}
                required={question.required}
              />
              <span>{option}</span>
            </label>
          ))}
        </div>
      )
    case 'trueFalse':
      return (
        <div className="ticket-intake-options" role="radiogroup" aria-labelledby={`ticket-q-label-${question.id}`}>
          <label className="ticket-intake-option">
            <input
              type="radio"
              name={`ticket-q-${question.id}`}
              checked={value === true}
              onChange={() => onChange(true)}
              required={question.required}
            />
            <span>True</span>
          </label>
          <label className="ticket-intake-option">
            <input
              type="radio"
              name={`ticket-q-${question.id}`}
              checked={value === false}
              onChange={() => onChange(false)}
              required={question.required}
            />
            <span>False</span>
          </label>
        </div>
      )
    case 'checkbox':
      return (
        <label className="ticket-intake-option">
          <input
            id={`ticket-q-${question.id}`}
            type="checkbox"
            checked={value === true}
            onChange={(event) => onChange(event.target.checked)}
          />
          <span>{question.aiOptOut ? 'Opt out of AI decision-making' : 'Yes'}</span>
        </label>
      )
    case 'link':
      return (
        <input
          id={`ticket-q-${question.id}`}
          type="url"
          className="sm-input"
          placeholder="https://"
          value={typeof value === 'string' ? value : ''}
          onChange={(event) => onChange(event.target.value)}
          required={question.required}
        />
      )
    case 'fileUpload':
    case 'messageForward':
    case 'mixed':
      return (
        <TicketIntakeFileUploadField question={question} value={value} onChange={onChange} />
      )
    case 'date':
      return (
        <input
          id={`ticket-q-${question.id}`}
          type="date"
          className="sm-input"
          value={typeof value === 'string' ? value : ''}
          onChange={(event) => onChange(event.target.value)}
          required={question.required}
        />
      )
    case 'multiSelect':
      return (
        <div className="ticket-intake-options" role="group" aria-labelledby={`ticket-q-label-${question.id}`}>
          {options.map((option) => {
            const selected = Array.isArray(value) ? value.includes(option) : false
            return (
              <label key={option} className="ticket-intake-option">
                <input
                  type="checkbox"
                  checked={selected}
                  onChange={(event) => {
                    const current = Array.isArray(value) ? value : []
                    if (event.target.checked) onChange([...current, option])
                    else onChange(current.filter((entry) => entry !== option))
                  }}
                />
                <span>{option}</span>
              </label>
            )
          })}
        </div>
      )
    case 'dropdown':
      return (
        <select
          id={`ticket-q-${question.id}`}
          className="sm-input"
          value={typeof value === 'string' ? value : ''}
          onChange={(event) => onChange(event.target.value)}
          required={question.required}
        >
          <option value="">Select…</option>
          {options.map((option) => (
            <option key={option} value={option}>{option}</option>
          ))}
        </select>
      )
    default:
      return null
  }
}

function TicketIntakeFileUploadField({
  question,
  value,
  onChange,
}: {
  question: TicketIntakeQuestion
  value: TicketAnswers[string]
  onChange: (next: TicketAnswers[string]) => void
}) {
  const [uploadError, setUploadError] = useState<string | null>(null)

  async function appendUploaded(parts: Array<{ kind: 'file'; attachmentId: string; fileName: string }>) {
    const existing = Array.isArray(value) ? value : []
    onChange([...existing, ...parts])
  }

  async function uploadFiles(files: File[]) {
    setUploadError(null)
    const uploaded: Array<{ kind: 'file'; attachmentId: string; fileName: string }> = []
    for (const file of files) {
      try {
        const { data } = await chatApi.uploadAttachment(file)
        uploaded.push({ kind: 'file', attachmentId: data.attachmentId, fileName: data.fileName })
      } catch {
        setUploadError('Could not upload this file. Please try again.')
        return
      }
    }
    if (uploaded.length > 0)
      await appendUploaded(uploaded)
  }

  return (
    <div className="ticket-intake-mixed">
      {(question.type === 'fileUpload' || question.allowedResponseKinds?.includes('file') || question.type === 'mixed') && (
        <input
          type="file"
          className="sm-input"
          multiple
          onChange={(event) => {
            const files = Array.from(event.target.files ?? [])
            event.target.value = ''
            if (files.length === 0)
              return
            void (async () => {
              await uploadFiles(files)
            })()
          }}
        />
      )}
      {uploadError && <p className="chat-composer-attach-error">{uploadError}</p>}
      {(question.allowedResponseKinds?.includes('link') || question.type === 'mixed') && (
        <input
          type="url"
          className="sm-input"
          placeholder="Add link https://"
          onBlur={(event) => {
            const url = event.target.value.trim()
            if (!url) return
            const existing = Array.isArray(value) ? value : []
            onChange([...existing, { kind: 'link', url }])
            event.target.value = ''
          }}
        />
      )}
      {(question.type === 'messageForward' || question.allowedResponseKinds?.includes('forward') || question.type === 'mixed') && (
        <p className="dashboard-hint">
          Use Report on a chat message to prefill a forwarded proof, or paste a message id below.
        </p>
      )}
      {Array.isArray(value) && value.length > 0 && (
        <ul className="ticket-intake-parts">
          {value.map((part, index) => (
            <li key={index}>{typeof part === 'object' && part && 'kind' in part ? String((part as { kind: string }).kind) : String(part)}</li>
          ))}
        </ul>
      )}
    </div>
  )
}

export function TicketIntakeWizard({
  portalRoomId,
  questions,
  initialAnswers = {},
  onCancel,
  onOpened,
}: TicketIntakeWizardProps) {
  const [answers, setAnswers] = useState<TicketAnswers>(initialAnswers)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState('')
  const [userQuery, setUserQuery] = useState('')
  const [userHits, setUserHits] = useState<{ userId: string; username: string; email: string }[]>([])

  function setAnswer(questionId: string, value: TicketAnswers[string]) {
    setAnswers((prev) => ({ ...prev, [questionId]: value }))
  }

  async function handleSubmit() {
    const validationError = validateAnswers(questions, answers)
    if (validationError) {
      setError(validationError)
      return
    }

    setSubmitting(true)
    setError('')
    try {
      const { data } = await ticketsApi.open(portalRoomId, answers)
      onOpened(data)
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { message?: string } } })?.response?.data?.message
        ?? 'Could not open the ticket. Check required answers and try again.'
      setError(msg)
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="ticket-intake-wizard" role="dialog" aria-modal="true" aria-labelledby="ticket-intake-title">
      <div className="ticket-intake-wizard-inner">
        <h3 id="ticket-intake-title">Open a ticket</h3>
        {questions.length === 0 && (
          <p className="server-page-subtitle">No intake questions — you can create the ticket right away.</p>
        )}
        {questions.map((question) => (
          <div key={question.id} className="sm-field ticket-intake-field">
            <label id={`ticket-q-label-${question.id}`} htmlFor={`ticket-q-${question.id}`} className="sm-label">
              {question.prompt}
              {question.required && <span className="sm-required">required</span>}
            </label>
            {needsOptions(question.type) && (question.options ?? []).length === 0 && (
              <p className="ticket-intake-missing-options">This question has no options configured.</p>
            )}
            {question.tracksUser ? (
              <div>
                <input
                  type="text"
                  className="sm-input"
                  placeholder="Search @username or email prefix"
                  value={userQuery}
                  onChange={(event) => {
                    const next = event.target.value
                    setUserQuery(next)
                    void (async () => {
                      if (!next.trim()) {
                        setUserHits([])
                        return
                      }
                      const { chatApi } = await import('../../api/chatApi')
                      const { data } = await chatApi.searchUsers(next.trim())
                      setUserHits(data.map((user) => ({
                        userId: user.userId,
                        username: user.username,
                        email: user.email,
                      })))
                    })()
                  }}
                />
                {userHits.length > 0 && (
                  <ul className="ticket-user-hits">
                    {userHits.map((user) => (
                      <li key={user.userId}>
                        <button
                          type="button"
                          className="btn-secondary"
                          onClick={() => {
                            setAnswer(question.id, user.userId)
                            setUserQuery(user.username)
                            setUserHits([])
                          }}
                        >
                          @{user.username}
                        </button>
                      </li>
                    ))}
                  </ul>
                )}
                {typeof answers[question.id] === 'string' && answers[question.id] !== '' ? (
                  <p className="dashboard-hint">Selected user id: {String(answers[question.id])}</p>
                ) : null}
              </div>
            ) : (
              <QuestionField
                question={question}
                value={answers[question.id]}
                onChange={(next) => setAnswer(question.id, next)}
              />
            )}
          </div>
        ))}
        {error && <p className="error">{error}</p>}
        <div className="ticket-intake-actions">
          <button type="button" className="btn-secondary" onClick={onCancel} disabled={submitting}>
            Cancel
          </button>
          <button type="button" className="btn-primary" onClick={() => void handleSubmit()} disabled={submitting}>
            {submitting ? 'Opening…' : 'Create ticket'}
          </button>
        </div>
      </div>
    </div>
  )
}
