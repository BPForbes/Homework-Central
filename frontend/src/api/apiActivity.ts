const MUTATING_METHODS = new Set(['post', 'put', 'patch', 'delete'])
const MIN_EVENT_GAP_MS = 180
let lastEmission = 0

export function emitApiMutation(method?: string, url?: string): void {
  if (!method || !MUTATING_METHODS.has(method.toLowerCase()))
    return
  if (url?.endsWith('/auth/refresh'))
    return
  if (window.matchMedia('(prefers-reduced-motion: reduce)').matches)
    return

  const now = performance.now()
  if (now - lastEmission < MIN_EVENT_GAP_MS)
    return
  lastEmission = now
  document.dispatchEvent(new CustomEvent('hc:api-mutate'))
}
