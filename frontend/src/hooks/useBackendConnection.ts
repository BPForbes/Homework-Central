import { useEffect, useState } from 'react'
import { checkBackendHealth } from '../utils/healthCheck'

const DEFAULT_POLL_MS = 1000

/** Polls /healthz until the backend accepts connections. */
export function useBackendConnection(pollIntervalMs = DEFAULT_POLL_MS) {
  const [isConnected, setIsConnected] = useState(false)

  useEffect(() => {
    let cancelled = false

    async function pollUntilReady() {
      while (!cancelled) {
        const result = await checkBackendHealth()
        if (cancelled)
          return
        if (result) {
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
