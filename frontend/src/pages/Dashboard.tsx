import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { MessageSquare, Shield } from 'lucide-react'
import { useAuth } from '../context/AuthContext'
import { useCaptcha } from '../hooks/useCaptcha'
import { Captcha } from '../components/Captcha'
import { captchaApi } from '../api/captchaApi'
import { subjectsApi } from '../api/subjectsApi'
import { inboxApi } from '../api/inboxApi'
import { GUEST_ROLE_BIT } from '../constants/roles'
import { Button } from '../components/ui/button'
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
    <div className="max-w-3xl mx-auto space-y-6">
      <div>
        <h2 className="text-2xl font-semibold text-foreground">Welcome, {user?.username}!</h2>
        <p className="text-muted-foreground mt-2">
          Open <Link to="/chat" className="text-primary font-medium hover:underline">Chat</Link> to browse subject and staff rooms.
        </p>
      </div>

      {inboxSummary.length > 0 && (
        <section className="bg-card border border-border rounded-lg p-5">
          <h3 className="font-semibold text-foreground mb-3">New messages</h3>
          <ul className="space-y-2">
            {inboxSummary.map((item) => (
              <li key={item.categoryKey}>
                <Link
                  to="/inbox"
                  className="block px-4 py-2.5 rounded-lg bg-amber-50 text-amber-900 font-medium text-sm hover:bg-amber-100 transition-colors"
                >
                  New Message ({item.categoryDisplayName}): {item.unreadCount}
                </Link>
              </li>
            ))}
          </ul>
        </section>
      )}

      <section className="flex gap-4 bg-card border border-border rounded-lg p-5">
        <div className="w-11 h-11 rounded-xl bg-primary/10 text-primary flex items-center justify-center shrink-0">
          <MessageSquare size={20} />
        </div>
        <div>
          <h3 className="font-semibold text-foreground">Chat rooms</h3>
          <p className="text-sm text-muted-foreground mt-1">
            Pick a channel from the sidebar — for example Calculus under Mathematics or Biology under Science.
          </p>
        </div>
      </section>

      {isGuest && (
        <section className="flex gap-4 bg-card border border-border rounded-lg p-5">
          <div className="w-11 h-11 rounded-xl bg-primary/10 text-primary flex items-center justify-center shrink-0">
            <Shield size={20} />
          </div>
          <div className="flex-1">
            <h3 className="font-semibold text-foreground">Verify your account</h3>
            <p className="text-sm text-muted-foreground mt-1 mb-3">Solve the challenge below to become a Verified User.</p>
            <form onSubmit={handleVerify} className="space-y-3">
              <Captcha captcha={captcha} disabled={verifying} />
              {verifyError && <p className="text-sm text-destructive">{verifyError}</p>}
              <Button type="submit" disabled={verifying || !captcha.canSubmit}>
                {verifying ? 'Verifying…' : 'Verify'}
              </Button>
            </form>
          </div>
        </section>
      )}

      <section>
        <h3 className="font-semibold text-foreground mb-2">Your roles</h3>
        {user?.roles?.length ? (
          <ul className="flex flex-wrap gap-2">
            {user.roles.map((r) => (
              <li key={r} className="px-3 py-1 rounded-full bg-secondary text-primary text-sm font-medium">
                {r}
              </li>
            ))}
          </ul>
        ) : (
          <p className="text-sm text-muted-foreground">No platform roles assigned.</p>
        )}
        {claimedSubjects.length > 0 && (
          <div className="mt-4">
            <h4 className="text-sm font-semibold text-foreground mb-2">Subject interests</h4>
            <ul className="flex flex-wrap gap-2">
              {claimedSubjects.map((name) => (
                <li key={name} className="px-3 py-1 rounded-full bg-muted text-foreground text-sm">
                  {name}
                </li>
              ))}
            </ul>
          </div>
        )}
        <p className="text-sm text-muted-foreground mt-3">
          Claim more subject interests from{' '}
          <Link to="/get-roles" className="text-primary font-medium hover:underline">
            Get Roles
          </Link>
          .
        </p>
      </section>
    </div>
  )
}
