import { useEffect, useState } from 'react'
import { checkBackendHealth } from '../utils/healthCheck'

const DEFAULT_POLL_MS = 2000

/**
 * Polls /healthz until the backend accepts connections.
 * The API may take several minutes on first dev startup while persona databases provision.
 */
export function useBackendConnection(pollIntervalMs = DEFAULT_POLL_MS) {
  const [isConnected, setIsConnected] = useState(false)

  useEffect(() => {
    let cancelled = false

    async function pollUntilReady() {
      while (!cancelled) {
        const ready = await checkBackendHealth()
        if (cancelled)
          return
        if (ready) {
          setIsConnected(true)
          return
        }
        await new Promise((resolve) => setTimeout(resolve, pollIntervalMs))
      }
    }

    void pollUntilReady()
    return () => {
      cancelled = true
    }
  }, [pollIntervalMs])

  return { isConnected }
}
