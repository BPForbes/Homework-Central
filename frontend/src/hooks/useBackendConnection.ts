import { useEffect, useState } from 'react'
import {
  delay,
  nextUnreachableBackoffMs,
  probeBackendHealth,
} from '../utils/healthCheck'

/** Budget for the API process to start listening. First-run migrate/seed is not this limit. */
const DEFAULT_DEADLINE_MS = 180_000

export type BackendConnectionPhase = 'connecting' | 'starting' | 'ready' | 'error'

/**
 * Waits until /healthz reports ready. The listen deadline applies only while
 * probes are unreachable — first-run migrate/seed can stay `starting` longer
 * than that budget. Backs off while the process is unreachable so Vite does
 * not log hundreds of ECONNREFUSED lines.
 */
export function useBackendConnection(deadlineMs = DEFAULT_DEADLINE_MS) {
  const [phase, setPhase] = useState<BackendConnectionPhase>('connecting')
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    const deadline = Date.now() + deadlineMs
    let unreachableAttempts = 0

    async function pollUntilReady() {
      while (!controller.signal.aborted) {
        const probe = await probeBackendHealth(undefined, controller.signal)
        if (controller.signal.aborted) {
          return
        }

        switch (probe.kind) {
          case 'ready':
            setPhase('ready')
            setError(null)
            return
          case 'starting':
            unreachableAttempts = 0
            setPhase('starting')
            setError(null)
            try {
              await delay(400, controller.signal)
            } catch {
              return
            }
            break
          case 'failed':
            setPhase('error')
            setError(probe.message)
            return
          case 'unreachable':
            if (Date.now() > deadline) {
              setPhase('error')
              setError(
                'Could not reach the backend API. Check that it is running and reachable, then reload the page.',
              )
              return
            }
            unreachableAttempts += 1
            setPhase('connecting')
            try {
              await delay(nextUnreachableBackoffMs(unreachableAttempts), controller.signal)
            } catch {
              return
            }
            break
        }
      }
    }

    setError(null)
    setPhase('connecting')
    void pollUntilReady()
    return () => controller.abort()
  }, [deadlineMs])

  return {
    isConnected: phase === 'ready',
    phase,
    error,
  }
}
