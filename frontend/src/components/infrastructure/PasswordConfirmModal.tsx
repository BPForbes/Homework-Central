import { useEffect, useRef, useState } from 'react'

interface PasswordConfirmModalProps {
  open: boolean
  title: string
  message: string
  onConfirm: (password: string) => void | Promise<void>
  onCancel: () => void
}

export function PasswordConfirmModal({
  open,
  title,
  message,
  onConfirm,
  onCancel,
}: PasswordConfirmModalProps) {
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const inputRef = useRef<HTMLInputElement>(null)
  const previousFocusRef = useRef<HTMLElement | null>(null)

  useEffect(() => {
    if (!open)
      return

    previousFocusRef.current = document.activeElement as HTMLElement | null
    inputRef.current?.focus()

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape')
        onCancel()
    }

    document.addEventListener('keydown', handleKeyDown)
    return () => {
      document.removeEventListener('keydown', handleKeyDown)
      previousFocusRef.current?.focus()
    }
  }, [open, onCancel])

  if (!open) return null

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setError('')
    setSubmitting(true)
    try {
      await onConfirm(password)
      setPassword('')
    } catch {
      setError('Password confirmation failed.')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="modal-backdrop" role="presentation" onClick={onCancel}>
      <div
        className="modal-panel"
        role="dialog"
        aria-modal="true"
        aria-labelledby="password-modal-title"
        onClick={(e) => e.stopPropagation()}
      >
        <h3 id="password-modal-title">{title}</h3>
        <p className="modal-message">{message}</p>
        <form onSubmit={handleSubmit}>
          <label htmlFor="confirm-password">Your password</label>
          <input
            ref={inputRef}
            id="confirm-password"
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete="current-password"
            required
          />
          {error && <p className="error">{error}</p>}
          <div className="modal-actions">
            <button type="button" className="btn-secondary" onClick={onCancel}>
              Cancel
            </button>
            <button type="submit" className="btn-primary" disabled={submitting}>
              {submitting ? 'Confirming…' : 'Confirm'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
