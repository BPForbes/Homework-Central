import { useState } from 'react'
import { Link } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faComments, faShieldHalved } from '@fortawesome/free-solid-svg-icons'
import { useAuth } from '../context/AuthContext'
import { useCaptcha } from '../hooks/useCaptcha'
import { Captcha } from '../components/Captcha'
import { captchaApi } from '../api/captchaApi'
import { GUEST_ROLE_BIT } from '../constants/roles'

export function Dashboard() {
  const { user, hasRole, refreshUser } = useAuth()
  const captcha = useCaptcha()
  const [verifying, setVerifying] = useState(false)
  const [verifyError, setVerifyError] = useState('')

  const isGuest = hasRole(GUEST_ROLE_BIT)

  async function handleVerify(e: React.FormEvent) {
    e.preventDefault()
    if (!captcha.canSubmit) {
      setVerifyError('Complete the verification check before submitting.')
      return
    }

    const submission = captcha.buildSubmission()
    if (!submission) {
      setVerifyError('Complete the verification check before submitting.')
      return
    }

    setVerifying(true)
    setVerifyError('')
    try {
      await captchaApi.verifyRole(submission)
      try {
        await refreshUser()
      } catch {
        // Verification succeeded server-side; local user refresh can be retried later.
      }
    } catch {
      setVerifyError("We couldn't verify you're human. Try the new challenge below.")
      void captcha.refresh()
    } finally {
      setVerifying(false)
    }
  }

  return (
    <div className="dashboard-content">
      <h2>Welcome, {user?.username}!</h2>
      <p className="dashboard-hint">
        Open the <strong>Chats</strong> menu on the left to browse subject and staff rooms you can access.
      </p>

      <section className="dashboard-card">
        <div className="dashboard-card-icon">
          <FontAwesomeIcon icon={faComments} />
        </div>
        <div>
          <h3>Chat rooms</h3>
          <p>Use the sliding panel to pick a room — for example Calculus under Mathematics or Biology under Science.</p>
        </div>
      </section>

      {isGuest && (
        <section className="dashboard-card verify-card">
          <div className="dashboard-card-icon">
            <FontAwesomeIcon icon={faShieldHalved} />
          </div>
          <div className="verify-card-body">
            <h3>Verify your account</h3>
            <p>Solve the challenge below to become a Verified User.</p>
            <form onSubmit={handleVerify}>
              <Captcha captcha={captcha} disabled={verifying} />
              {verifyError && <p className="error">{verifyError}</p>}
              <button type="submit" className="btn-primary" disabled={verifying || !captcha.canSubmit}>
                {verifying ? 'Verifying…' : 'Verify'}
              </button>
            </form>
          </div>
        </section>
      )}

      <section className="roles-section">
        <h3>Your roles</h3>
        {user?.roles?.length ? (
          <ul>
            {user.roles.map((r) => (
              <li key={r}>{r}</li>
            ))}
          </ul>
        ) : (
          <p>No roles assigned.</p>
        )}
      </section>

      <p className="dashboard-footer-link">
        <Link to="/chat">Browse chats</Link>
      </p>
    </div>
  )
}
