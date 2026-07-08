/**
 * Localhost-only developer sign-in page (VITE_HC_DEV_BYPASS).
 * Select a developer account and optionally impersonate a persona; blank user uses DevAdmin.
 */
import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../context/AuthContext'
import { authApi } from '../api/authApi'
import type { DevDeveloperOption, DevStatus } from '../types/devAuth'

import { Button } from '../components/ui/button'
import { Label } from '../components/ui/label'
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
        await new Promise((resolve) => setTimeout(resolve, 2000))
      }
    }

    void pollUntilReady()
    return () => {
      cancelled = true
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
    <div className="min-h-screen flex items-center justify-center bg-background p-4">
      <div className="bg-card rounded-lg shadow-sm border border-border p-10 w-full max-w-[420px]">
        <p className="text-sm font-medium text-primary mb-1">Homework Central</p>
        <h1 className="text-2xl font-semibold text-foreground mb-7">Developer sign in</h1>

        {personaStatus && personaStatus.personasReady === false && personaStatus.personasTotal && (
          <p className="text-sm text-muted-foreground mb-4">
            Loading dev personas ({personaStatus.personasProvisioned ?? 0}/{personaStatus.personasTotal})…
          </p>
        )}

        {setupError && <p className="text-sm text-destructive mb-4">{setupError}</p>}

        <form onSubmit={handleSubmit} className="space-y-4">
          <div className="space-y-1.5">
            <Label htmlFor="developer">Developer account</Label>
            <select
              id="developer"
              value={developerUserId}
              onChange={(e) => setDeveloperUserId(e.target.value)}
              disabled={loginDisabled}
              className="flex h-10 w-full rounded-lg border border-border bg-input-background px-3 py-2 text-sm disabled:opacity-50"
            >
              {developers.length === 0 && <option value="">No developers found</option>}
              {developers.map((dev) => (
                <option key={dev.userId} value={dev.userId}>
                  {dev.username}
                </option>
              ))}
            </select>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="targetUser">Sign in as user (optional)</Label>
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
              className="flex h-10 w-full rounded-lg border border-border bg-input-background px-3 py-2 text-sm disabled:opacity-50"
            >
              <option value="">—</option>
              {availableUsers.map((user) => (
                <option key={`${user.tenantDatabaseName}:${user.userId}`} value={`${user.tenantDatabaseName}:${user.userId}`}>
                  {user.username}
                </option>
              ))}
            </select>
          </div>

          {error && <p className="text-sm text-destructive">{error}</p>}

          <Button type="submit" className="w-full" disabled={loginDisabled}>
            {isSubmitting ? 'Signing in…' : isLoadingOptions ? 'Loading…' : 'Sign in'}
          </Button>
        </form>
      </div>
    </div>
  )
}
