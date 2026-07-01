export interface BackendHealth {
  status: string
  personasProvisioned?: number
  personasTotal?: number
  personasReady?: boolean
}

/** Returns health payload when the proxied API /healthz endpoint responds OK. */
export async function checkBackendHealth(timeoutMs = 5000): Promise<BackendHealth | null> {
  try {
    const response = await fetch('/healthz', { signal: AbortSignal.timeout(timeoutMs) })
    if (!response.ok)
      return null

    return await response.json() as BackendHealth
  } catch {
    return null
  }
}
