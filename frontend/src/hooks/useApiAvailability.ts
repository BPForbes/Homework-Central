import { useEffect, useState } from 'react'

/** Polls the proxied API /healthz endpoint until the backend is reachable. */
export function useApiAvailability(pollIntervalMs = 3000) {
  const [apiUnavailable, setApiUnavailable] = useState(false)
  const [isChecking, setIsChecking] = useState(true)

  useEffect(() => {
    let cancelled = false

    async function isApiReachable(): Promise<boolean> {
      try {
        const response = await fetch('/healthz', { signal: AbortSignal.timeout(5000) })
        return response.ok
      } catch {
        return false
      }
    }

    async function pollUntilReady() {
      while (!cancelled) {
        setIsChecking(true)
        const ready = await isApiReachable()
        if (cancelled) return
        setApiUnavailable(!ready)
        setIsChecking(false)
        if (ready) return
        await new Promise((resolve) => setTimeout(resolve, pollIntervalMs))
      }
    }

    void pollUntilReady()
    return () => {
      cancelled = true
    }
  }, [pollIntervalMs])

  return { apiUnavailable, isChecking }
}
