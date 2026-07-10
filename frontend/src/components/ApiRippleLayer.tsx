import { useEffect, useRef } from 'react'

export function ApiRippleLayer() {
  const layerRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const layer = layerRef.current
    if (!layer)
      return

    const handleMutation = () => {
      if (layer.childElementCount > 2)
        return

      const burst = document.createElement('span')
      burst.className = 'api-droplet-burst'
      const offset = Math.round((Math.random() - 0.5) * 24)
      burst.style.setProperty('--droplet-offset', `${offset}vw`)
      burst.addEventListener('animationend', () => burst.remove(), { once: true })
      layer.appendChild(burst)
    }

    document.addEventListener('hc:api-mutate', handleMutation)
    return () => document.removeEventListener('hc:api-mutate', handleMutation)
  }, [])

  return <div ref={layerRef} className="api-ripple-layer" aria-hidden="true" />
}
