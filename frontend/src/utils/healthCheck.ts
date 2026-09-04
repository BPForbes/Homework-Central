export type BackendHealthStatus = 'healthy' | 'starting' | 'failed' | string

export interface BackendHealth {
  status: BackendHealthStatus
  ready?: boolean
  failure?: string | null
  personasProvisioned?: number
  personasTotal?: number
  personasReady?: boolean
}

export type BackendHealthProbe =
  | { kind: 'ready'; health: BackendHealth }
  | { kind: 'starting'; health: BackendHealth }
  | { kind: 'failed'; health: BackendHealth; message: string }
  | { kind: 'unreachable' }

/** Short probe timeout while the API process may not be listening yet. */
export const HEALTH_PROBE_TIMEOUT_MS = 800

/**
 * Probes /healthz through the Vite proxy.
 * Distinguishes unreachable (ECONNREFUSED) from listening-but-warming (`starting`).
 */
export async function probeBackendHealth(
  timeoutMs = HEALTH_PROBE_TIMEOUT_MS,
  signal?: AbortSignal,
): Promise<BackendHealthProbe> {
  try {
    const timeoutSignal = AbortSignal.timeout(timeoutMs)
    const response = await fetch('/healthz', {
      signal: signal ? AbortSignal.any([signal, timeoutSignal]) : timeoutSignal,
    })

    let health: BackendHealth
    try {
      health = (await response.json()) as BackendHealth
    } catch {
      return { kind: 'unreachable' }
    }

    const status = health.status
    if (status === 'failed' || response.status === 503) {
      return {
        kind: 'failed',
        health,
        message: health.failure?.trim() || 'Backend startup failed.',
      }
    }

    if (status === 'healthy' || health.ready === true) {
      return { kind: 'ready', health }
    }

    if (status === 'starting' || response.ok) {
      return { kind: 'starting', health }
    }

    return { kind: 'unreachable' }
  } catch {
    return { kind: 'unreachable' }
  }
}

/** Resolves after `ms` or rejects when `signal` is aborted. */
export function delay(ms: number, signal?: AbortSignal): Promise<void> {
  if (signal?.aborted) {
    return Promise.reject(new DOMException('Aborted', 'AbortError'))
  }

  return new Promise((resolve, reject) => {
    const timer = window.setTimeout(() => {
      signal?.removeEventListener('abort', onAbort)
      resolve()
    }, ms)

    function onAbort() {
      window.clearTimeout(timer)
      reject(new DOMException('Aborted', 'AbortError'))
    }

    signal?.addEventListener('abort', onAbort, { once: true })
  })
}

/** Exponential backoff between unreachable probes to avoid flooding the Vite proxy log. */
export function nextUnreachableBackoffMs(attempt: number): number {
  const cappedAttempt = Math.min(Math.max(attempt, 1), 8)
  return Math.min(250 * 2 ** (cappedAttempt - 1), 3000)
}
