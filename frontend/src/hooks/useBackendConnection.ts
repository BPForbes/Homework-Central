import { useEffect, useState } from 'react'
import { checkBackendHealth, delay } from '../utils/healthCheck'

const DEFAULT_POLL_MS = 1000
const DEFAULT_MAX_ATTEMPTS = 60 // ~1 minute at the default poll interval

/**
 * Polls /healthz until the backend accepts connections. Gives up after `maxAttempts` failed
 * checks (instead of polling forever) and reports that as `error`, so BackendGate can show a
 * meaningful message instead of an indefinite spinner if the API never comes up.
 */
export function useBackendConnection(pollIntervalMs = DEFAULT_POLL_MS, maxAttempts = DEFAULT_MAX_ATTEMPTS) {
  const [isConnected, setIsConnected] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()

    async function pollUntilReady() {
      for (let attempt = 1; attempt <= maxAttempts; attempt++) {
        if (controller.signal.aborted)
          return

        const result = await checkBackendHealth(undefined, controller.signal)
        if (controller.signal.aborted)
          return
        if (result) {
          setIsConnected(true)
          setError(null)
          return
        }
        if (attempt >= maxAttempts) {
          setError('Could not reach the backend API. Check that it is running and reachable, then reload the page.')
          return
        }

        try {
          await delay(pollIntervalMs, controller.signal)
        } catch {
          return
        }
      }
    }

    setError(null)
    void pollUntilReady()
    return () => controller.abort()
  }, [pollIntervalMs, maxAttempts])

  return { isConnected, error }
}
