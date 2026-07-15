export interface BackendHealth {
  status: string
  personasProvisioned?: number
  personasTotal?: number
  personasReady?: boolean
}

/** Returns health payload when the proxied API /healthz endpoint responds OK. */
export async function checkBackendHealth(
  timeoutMs = 5000,
  signal?: AbortSignal,
): Promise<BackendHealth | null> {
  try {
    const timeoutSignal = AbortSignal.timeout(timeoutMs)
    const response = await fetch('/healthz', {
      signal: signal ? AbortSignal.any([signal, timeoutSignal]) : timeoutSignal,
    })
    if (!response.ok)
      return null

    return await response.json() as BackendHealth
  } catch {
    return null
  }
}

/** Resolves after `ms` or rejects when `signal` is aborted. */
export function delay(ms: number, signal?: AbortSignal): Promise<void> {
  if (signal?.aborted)
    return Promise.reject(new DOMException('Aborted', 'AbortError'))

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
