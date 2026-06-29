/**
 * Localhost-only developer sign-in page (VITE_HC_DEV_BYPASS).
 * Select a developer account and optionally impersonate a persona; blank user uses DevAdmin.
 */
import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../context/AuthContext'
import { authApi } from '../api/authApi'
import type { DevDeveloperOption } from '../types/devAuth'

const DEV_BYPASS_ENABLED = import.meta.env.VITE_HC_DEV_BYPASS === 'true'

export function DevLogin() {
  const { devLogin } = useAuth()
  const navigate = useNavigate()
  const [developers, setDevelopers] = useState<DevDeveloperOption[]>([])
  const [developerUserId, setDeveloperUserId] = useState('')
  const [targetUserId, setTargetUserId] = useState('')
  const [error, setError] = useState('')
  const [apiUnavailable, setApiUnavailable] = useState(false)
  const [isLoadingOptions, setIsLoadingOptions] = useState(true)
  const [isSubmitting, setIsSubmitting] = useState(false)

  const selectedDeveloper = useMemo(
    () => developers.find((developer) => developer.userId === developerUserId) ?? null,
    [developers, developerUserId]
  )

  const availableUsers = selectedDeveloper?.users ?? []

  useEffect(() => {
    if (!DEV_BYPASS_ENABLED) {
      navigate('/login', { replace: true })
      return
    }

    let cancelled = false

    async function loadOptions() {
      setIsLoadingOptions(true)
      setApiUnavailable(false)
      setError('')
      try {
        const { data } = await authApi.devStatus()
        if (!data.available) {
          if (!cancelled) setApiUnavailable(true)
          return
        }
        const options = await authApi.devOptions()
        if (cancelled) return
        setDevelopers(options.data.developers)
        if (options.data.developers.length > 0) {
          setDeveloperUserId(options.data.developers[0].userId)
        }
      } catch (err: unknown) {
        if (cancelled) return
        const message =
          (err as { response?: { data?: { message?: string } } })?.response?.data?.message
        if (message) {
          setError(message)
        } else {
          setApiUnavailable(true)
        }
      } finally {
        if (!cancelled) setIsLoadingOptions(false)
      }
    }

    void loadOptions()
    return () => {
      cancelled = true
    }
  }, [navigate])

  useEffect(() => {
    setTargetUserId('')
  }, [developerUserId])

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
                  {dev.username}
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
              disabled={loginDisabled || availableUsers.length === 0}
            >
              <option value="">—</option>
              {availableUsers.map((user) => (
                <option key={user.userId} value={user.userId}>
                  {user.username}
                </option>
              ))}
            </select>
          </div>

          {error && <p className="error">{error}</p>}

          <button type="submit" className="btn-primary" disabled={loginDisabled}>
            {isSubmitting ? 'Signing in…' : isLoadingOptions ? 'Loading…' : 'Sign in'}
          </button>
        </form>
      </div>
    </div>
  )
}
