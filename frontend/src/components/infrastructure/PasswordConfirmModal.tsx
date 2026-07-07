import { useState } from 'react'

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
        <p className="modal-hint">Dev accounts: use password <code>hcentralpassword</code>.</p>
        <form onSubmit={handleSubmit}>
          <label htmlFor="confirm-password">Your password</label>
          <input
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
