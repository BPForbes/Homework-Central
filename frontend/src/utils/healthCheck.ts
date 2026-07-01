/** Returns true when the proxied API /healthz endpoint responds OK. */
export async function checkBackendHealth(timeoutMs = 5000): Promise<boolean> {
  try {
    const response = await fetch('/healthz', { signal: AbortSignal.timeout(timeoutMs) })
    return response.ok
  } catch {
    return false
  }
}
