import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faShieldHalved } from '@fortawesome/free-solid-svg-icons'
import { useAuth } from '../context/AuthContext'
import { useCaptcha } from '../hooks/useCaptcha'
import { Captcha } from '../components/Captcha'
import { captchaApi } from '../api/captchaApi'
import { subjectsApi } from '../api/subjectsApi'
import { inboxApi } from '../api/inboxApi'
import { GUEST_ROLE_BIT } from '../constants/roles'
import { ServerMaintenanceNav } from '../components/layout/ServerMaintenanceNav'
import { byPrefixAndName } from '../icons/byPrefixAndName'
import type { ChatInboxSummaryItem } from '../types/inbox'

export function Dashboard() {
  const { user, hasRole, refreshUser } = useAuth()
  const captcha = useCaptcha()
  const [verifying, setVerifying] = useState(false)
  const [verifyError, setVerifyError] = useState('')
  const [claimedSubjects, setClaimedSubjects] = useState<string[]>([])
  const [inboxSummary, setInboxSummary] = useState<ChatInboxSummaryItem[]>([])

  const isGuest = hasRole(GUEST_ROLE_BIT)

  useEffect(() => {
    void subjectsApi
      .getGeneral()
      .then(({ data }) => setClaimedSubjects(data.filter((subject) => subject.claimed).map((subject) => subject.name)))
      .catch(() => setClaimedSubjects([]))
  }, [user?.userId])

  useEffect(() => {
    void inboxApi
      .getSummary()
      .then(({ data }) => setInboxSummary(data.categories))
      .catch(() => setInboxSummary([]))
  }, [user?.userId])

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
      <ServerMaintenanceNav title="Dashboard" />

      <h2>Welcome, {user?.username}!</h2>
      <p className="dashboard-hint">
        Use the <strong>Chats</strong> sidebar on the left to browse subject and staff rooms you can access.
      </p>

      {inboxSummary.length > 0 && (
        <section className="dashboard-inbox-summary">
          <h3>New messages</h3>
          <ul className="dashboard-inbox-list">
            {inboxSummary.map((item) => (
              <li key={item.categoryKey}>
                <Link to="/inbox" className="dashboard-inbox-link">
                  <FontAwesomeIcon icon={byPrefixAndName.fas.envelope} className="dashboard-inbox-icon" />
                  New Message ({item.categoryDisplayName}): {item.unreadCount}
                </Link>
              </li>
            ))}
          </ul>
        </section>
      )}

      <section className="dashboard-card">
        <div className="dashboard-card-icon">
          <FontAwesomeIcon icon={byPrefixAndName.far.comments} />
        </div>
        <div>
          <h3>Chat rooms</h3>
          <p>Pick a room from the sidebar — for example Calculus under Mathematics or Biology under Science.</p>
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
          <p>No platform roles assigned.</p>
        )}
        {claimedSubjects.length > 0 && (
          <>
            <h4>Subject interests</h4>
            <ul>
              {claimedSubjects.map((name) => (
                <li key={name}>{name}</li>
              ))}
            </ul>
          </>
        )}
        <p className="dashboard-hint">
          Claim more subject interests from <Link to="/get-roles">Get Roles</Link>.
        </p>
      </section>
    </div>
  )
}
