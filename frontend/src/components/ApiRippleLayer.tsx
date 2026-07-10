import { useEffect, useRef } from 'react'

const AMBIENT_MIN_MS = 5_000
const AMBIENT_MAX_MS = 11_000

function spawnDroplet(layer: HTMLElement, options?: { left?: string; bottom?: string }) {
  if (layer.childElementCount > 4)
    return

  const burst = document.createElement('span')
  burst.className = options ? 'api-droplet-burst api-droplet-burst--ambient' : 'api-droplet-burst'

  if (options?.left && options.bottom) {
    burst.style.left = options.left
    burst.style.bottom = options.bottom
  } else {
    const offset = Math.round((Math.random() - 0.5) * 24)
    burst.style.setProperty('--droplet-offset', `${offset}vw`)
  }

  burst.addEventListener('animationend', () => burst.remove(), { once: true })
  layer.appendChild(burst)
}

export function ApiRippleLayer() {
  const layerRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const layer = layerRef.current
    if (!layer)
      return

    const handleMutation = () => spawnDroplet(layer)

    document.addEventListener('hc:api-mutate', handleMutation)

    let ambientTimer: ReturnType<typeof setTimeout> | undefined
    let cancelled = false

    function scheduleAmbientDroplet() {
      if (cancelled || window.matchMedia('(prefers-reduced-motion: reduce)').matches)
        return

      const delay = AMBIENT_MIN_MS + Math.random() * (AMBIENT_MAX_MS - AMBIENT_MIN_MS)
      ambientTimer = window.setTimeout(() => {
        if (cancelled || !layer)
          return

        spawnDroplet(layer, {
          left: `${8 + Math.random() * 84}%`,
          bottom: `${6 + Math.random() * 72}%`,
        })
        scheduleAmbientDroplet()
      }, delay)
    }

    scheduleAmbientDroplet()

    return () => {
      cancelled = true
      document.removeEventListener('hc:api-mutate', handleMutation)
      if (ambientTimer !== undefined)
        window.clearTimeout(ambientTimer)
    }
  }, [])

  return <div ref={layerRef} className="api-ripple-layer" aria-hidden="true" />
}
