import { useEffect, useId, useRef, useState } from 'react'

interface FCaptchaWidgetProps {
  siteKey: string
  publicUrl: string
  onToken: (token: string) => void
  disabled?: boolean
}

interface FCaptchaGlobal {
  configure: (opts: { serverUrl: string }) => void
  render: (containerId: string, opts: { siteKey: string; callback: (token: string) => void }) => void
}

declare global {
  interface Window {
    FCaptcha?: FCaptchaGlobal
  }
}

const scriptPromises = new Map<string, Promise<void>>()

function loadScript(src: string): Promise<void> {
  const cached = scriptPromises.get(src)
  if (cached) return cached

  const promise = new Promise<void>((resolve, reject) => {
    const script = document.createElement('script')
    script.src = src
    script.async = true
    script.onload = () => resolve()
    script.onerror = () => {
      scriptPromises.delete(src)
      reject(new Error(`Failed to load ${src}`))
    }
    document.head.appendChild(script)
  })
  scriptPromises.set(src, promise)
  return promise
}

/**
 * The mandatory "I'm not a robot" checkbox, backed by a self-hosted FCaptcha instance
 * (https://github.com/WebDecoy/FCaptcha) rather than a third-party account. No npm client package
 * exists for it (unlike hCaptcha's @hcaptcha/react-hcaptcha), so this loads the widget script
 * directly from the configured server and drives its documented global `window.FCaptcha` API.
 * Render with `key={challengeId}` from the parent so a fresh challenge also gets a fresh widget
 * instance instead of reusing a stale one.
 */
export function FCaptchaWidget({ siteKey, publicUrl, onToken, disabled }: FCaptchaWidgetProps) {
  const containerId = `fcaptcha-${useId().replace(/[^a-zA-Z0-9]/g, '')}`
  const containerRef = useRef<HTMLDivElement>(null)
  const [status, setStatus] = useState<'loading' | 'ready' | 'error'>('loading')

  useEffect(() => {
    let cancelled = false
    setStatus('loading')

    loadScript(`${publicUrl.replace(/\/$/, '')}/fcaptcha.js`)
      .then(() => {
        if (cancelled || !window.FCaptcha || !containerRef.current) return
        window.FCaptcha.configure({ serverUrl: publicUrl })
        window.FCaptcha.render(containerId, {
          siteKey,
          callback: onToken,
        })
        setStatus('ready')
      })
      .catch(() => {
        if (!cancelled) setStatus('error')
      })

    return () => {
      cancelled = true
    }
  }, [siteKey, publicUrl, containerId, onToken])

  return (
    <div className="fcaptcha-widget" style={disabled ? { pointerEvents: 'none', opacity: 0.6 } : undefined}>
      <div id={containerId} ref={containerRef} className="fcaptcha-container" aria-busy={status === 'loading'} />
      {status === 'loading' && <p className="captcha-puzzle-status">Loading the &ldquo;I&apos;m not a robot&rdquo; check…</p>}
      {status === 'error' && <p className="error">Couldn&apos;t load the verification check. Please refresh and try again.</p>}
    </div>
  )
}
