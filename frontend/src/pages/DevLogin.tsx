import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../context/AuthContext'
import { authApi } from '../api/authApi'
import type { DevUserOption } from '../types/devAuth'

const DEV_BYPASS_ENABLED = import.meta.env.VITE_HC_DEV_BYPASS === 'true'

export function DevLogin() {
  const { devLogin } = useAuth()
  const navigate = useNavigate()
  const [developers, setDevelopers] = useState<DevUserOption[]>([])
  const [users, setUsers] = useState<DevUserOption[]>([])
  const [developerUserId, setDeveloperUserId] = useState('')
  const [targetUserId, setTargetUserId] = useState('')
  const [error, setError] = useState('')
  const [apiUnavailable, setApiUnavailable] = useState(false)
  const [isLoadingOptions, setIsLoadingOptions] = useState(true)
  const [isSubmitting, setIsSubmitting] = useState(false)

  useEffect(() => {
    if (!DEV_BYPASS_ENABLED) {
      navigate('/login', { replace: true })
      return
    }

    let cancelled = false

    async function loadOptions() {
      setIsLoadingOptions(true)
      setApiUnavailable(false)
      try {
        const { data } = await authApi.devStatus()
        if (!data.available) {
          if (!cancelled) setApiUnavailable(true)
          return
        }
        const options = await authApi.devOptions()
        if (cancelled) return
        setDevelopers(options.data.developers)
        setUsers(options.data.users)
        if (options.data.developers.length > 0) {
          setDeveloperUserId(options.data.developers[0].userId)
        }
      } catch {
        if (!cancelled) setApiUnavailable(true)
      } finally {
        if (!cancelled) setIsLoadingOptions(false)
      }
    }

    void loadOptions()
    return () => {
      cancelled = true
    }
  }, [navigate])

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setError('')

    if (apiUnavailable) return

    if (!developerUserId) {
      setError('Select a developer account.')
      return
    }

    setIsSubmitting(true)
    try {
      await devLogin(developerUserId, targetUserId || null)
      navigate('/dashboard')
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { message?: string } } })?.response?.data?.message ??
        'Developer login failed.'
      setError(msg)
    } finally {
      setIsSubmitting(false)
    }
  }

  if (!DEV_BYPASS_ENABLED) {
    return null
  }

  const loginDisabled = apiUnavailable || isLoadingOptions || isSubmitting

  return (
    <div className="auth-page">
      <div className="auth-card">
        <h1>Homework Central</h1>
        <h2>Developer sign in</h2>

        {apiUnavailable && (
          <div className="api-unavailable" role="alert">
            <span className="api-unavailable-icon" aria-hidden="true">
              <span className="api-unavailable-x">X</span>
            </span>
            <span>unable to connect to API</span>
          </div>
        )}

        <form onSubmit={handleSubmit}>
          <div className="field">
            <label htmlFor="developer">Developer account</label>
            <select
              id="developer"
              value={developerUserId}
              onChange={(e) => setDeveloperUserId(e.target.value)}
              disabled={loginDisabled}
            >
              {developers.length === 0 && <option value="">No developers found</option>}
              {developers.map((dev) => (
                <option key={dev.userId} value={dev.userId}>
                  {dev.username} ({dev.email})
                </option>
              ))}
            </select>
          </div>

          <div className="field">
            <label htmlFor="targetUser">Sign in as user (optional)</label>
            <select
              id="targetUser"
              value={targetUserId}
              onChange={(e) => setTargetUserId(e.target.value)}
              disabled={loginDisabled}
            >
              <option value="">DevAdmin (Owner permissions)</option>
              {users.map((user) => (
                <option key={user.userId} value={user.userId}>
                  {user.username} ({user.email})
                </option>
              ))}
            </select>
          </div>

          {error && <p className="error">{error}</p>}

          <button type="submit" className="btn-primary" disabled={loginDisabled}>
            {isSubmitting ? 'Signing in…' : isLoadingOptions ? 'Loading…' : 'Sign in'}
          </button>
        </form>

        <p className="auth-footer dev-login-hint">
          Local development only. Leave user blank to sign in as DevAdmin.
        </p>
      </div>
    </div>
  )
}
