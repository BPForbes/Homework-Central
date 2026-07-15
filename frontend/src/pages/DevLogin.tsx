/**
 * Localhost-only developer sign-in page (VITE_HC_DEV_BYPASS).
 * Select a developer account and optionally impersonate a persona; blank user uses DevAdmin.
 */
import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../context/useAuth'
import { authApi } from '../api/authApi'
import { delay } from '../utils/healthCheck'
import type { DevDeveloperOption, DevStatus } from '../types/devAuth'

const DEV_BYPASS_ENABLED = import.meta.env.VITE_HC_DEV_BYPASS === 'true'

export function DevLogin() {
  const { devLogin } = useAuth()
  const navigate = useNavigate()
  const [developers, setDevelopers] = useState<DevDeveloperOption[]>([])
  const [developerUserId, setDeveloperUserId] = useState('')
  const [targetUserId, setTargetUserId] = useState('')
  const [tenantDatabaseName, setTenantDatabaseName] = useState('')
  const [error, setError] = useState('')
  const [setupError, setSetupError] = useState('')
  const [isLoadingOptions, setIsLoadingOptions] = useState(true)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [personaStatus, setPersonaStatus] = useState<DevStatus | null>(null)

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
    const controller = new AbortController()

    // The readiness poll's own error state is kept separate from `error` (which also carries
    // submit-attempt failures set by handleSubmit): loadOptions previously called setError('')
    // on every tick, which would silently wipe out a "Developer login failed." message from a
    // submit that happened while background persona provisioning was still in progress.
    async function loadOptions(): Promise<boolean> {
      setIsLoadingOptions(true)
      setSetupError('')
      try {
        const statusResponse = await authApi.devStatus()
        if (cancelled)
          return false

        setPersonaStatus(statusResponse.data)

        if (!statusResponse.data.available) {
          setSetupError('Developer bypass is not enabled on the API.')
          return true
        }

        const options = await authApi.devOptions()
        if (cancelled)
          return false

        setDevelopers(options.data.developers)
        // Only default-select when nothing is chosen yet, or the previous selection vanished
        // from the list — otherwise every 2s poll tick (while personas are still provisioning
        // in the background) would silently override a developer the user already picked.
        setDeveloperUserId((current) => {
          if (current && options.data.developers.some((developer) => developer.userId === current))
            return current
          return options.data.developers[0]?.userId ?? ''
        })

        return statusResponse.data.personasReady ?? true
      } catch (err: unknown) {
        if (cancelled)
          return false
        const response = (err as { response?: { status?: number; data?: { message?: string } } })
          ?.response
        if (response?.status === 404) {
          setSetupError('Developer bypass is not enabled on the API.')
          return true
        }
        setSetupError(response?.data?.message ?? 'Failed to load developer accounts.')
        return true
      } finally {
        if (!cancelled)
          setIsLoadingOptions(false)
      }
    }

    async function pollUntilReady() {
      while (!cancelled) {
        const ready = await loadOptions()
        if (ready || cancelled)
          break
        try {
          await delay(2000, controller.signal)
        } catch {
          break
        }
      }
    }

    void pollUntilReady()
    return () => {
      cancelled = true
      controller.abort()
    }
  }, [navigate])

  useEffect(() => {
    setTargetUserId('')
    setTenantDatabaseName('')
  }, [developerUserId])

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setError('')

    if (!developerUserId) {
      setError('Select a developer account.')
      return
    }

    setIsSubmitting(true)
    try {
      await devLogin(developerUserId, targetUserId || null, tenantDatabaseName || null)
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

  if (!DEV_BYPASS_ENABLED)
    return null

  // Keep the button disabled whenever setup itself is broken (dev bypass unavailable, or no
  // developer accounts came back) — otherwise it re-enables the moment loading finishes, and
  // clicking it just overwrites the real setup error with a generic "Select a developer
  // account." from handleSubmit's own guard.
  const loginDisabled = isLoadingOptions || isSubmitting || Boolean(setupError) || developers.length === 0

  return (
    <div className="auth-page">
      <div className="auth-card">
        <h1>Homework Central</h1>
        <h2>Developer sign in</h2>

        {personaStatus && personaStatus.personasReady === false && personaStatus.personasTotal && (
          <p className="dev-persona-status">
            Loading dev personas ({personaStatus.personasProvisioned ?? 0}/{personaStatus.personasTotal})…
          </p>
        )}

        {setupError && <p className="error">{setupError}</p>}

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
              value={targetUserId ? `${tenantDatabaseName}:${targetUserId}` : ''}
              onChange={(e) => {
                const value = e.target.value
                if (!value) {
                  setTargetUserId('')
                  setTenantDatabaseName('')
                  return
                }

                const separatorIndex = value.indexOf(':')
                setTenantDatabaseName(value.slice(0, separatorIndex))
                setTargetUserId(value.slice(separatorIndex + 1))
              }}
              disabled={loginDisabled || availableUsers.length === 0}
            >
              <option value="">—</option>
              {availableUsers.map((user) => (
                <option key={`${user.tenantDatabaseName}:${user.userId}`} value={`${user.tenantDatabaseName}:${user.userId}`}>
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
