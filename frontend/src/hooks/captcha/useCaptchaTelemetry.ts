import { useCallback, useMemo, useRef } from 'react'
import type { CaptchaBehavior, MouseSample } from '../../types/captcha'

const MAX_MOUSE_SAMPLES = 400
const MOUSE_SAMPLE_INTERVAL_MS = 60

/**
 * Cross-cutting behavioral telemetry (mouse movement, keystroke timing, interaction count,
 * duration, navigator.webdriver) shared by every puzzle module — not owned by any one puzzle
 * type. The score itself is never computed here — only collected and sent for the server to
 * score, since a client-computed score could simply be fabricated by a bot.
 */
export function useCaptchaTelemetry() {
  const startedAtRef = useRef(0)
  const mouseSamplesRef = useRef<MouseSample[]>([])
  const lastSampleAtRef = useRef(0)
  const keyTimestampsRef = useRef<number[]>([])
  const interactionCountRef = useRef(0)
  const webdriverFlagRef = useRef(false)

  const reset = useCallback(() => {
    startedAtRef.current = performance.now()
    mouseSamplesRef.current = []
    lastSampleAtRef.current = 0
    keyTimestampsRef.current = []
    interactionCountRef.current = 0
    webdriverFlagRef.current =
      typeof navigator !== 'undefined' && Boolean((navigator as { webdriver?: boolean }).webdriver)
  }, [])

  const recordMouseMove = useCallback((x: number, y: number) => {
    const now = performance.now()
    if (now - lastSampleAtRef.current < MOUSE_SAMPLE_INTERVAL_MS) return
    lastSampleAtRef.current = now
    if (mouseSamplesRef.current.length >= MAX_MOUSE_SAMPLES) return
    mouseSamplesRef.current.push({ x, y, tMs: Math.round(now - startedAtRef.current) })
  }, [])

  const recordKeydown = useCallback(() => {
    keyTimestampsRef.current.push(performance.now())
  }, [])

  const recordInteraction = useCallback(() => {
    interactionCountRef.current += 1
  }, [])

  const buildBehavior = useCallback((): CaptchaBehavior => {
    const timestamps = keyTimestampsRef.current
    const keyIntervalsMs = timestamps.slice(1).map((t, i) => Math.round(t - timestamps[i]))

    return {
      mouseSamples: mouseSamplesRef.current,
      keyIntervalsMs,
      totalDurationMs: Math.round(performance.now() - startedAtRef.current),
      webdriverFlag: webdriverFlagRef.current,
      interactionCount: interactionCountRef.current,
    }
  }, [])

  // All five members are themselves referentially stable (each useCallback above has an empty
  // dependency array), so this object's identity is stable for the component's entire lifetime —
  // any consumer that depends on the whole returned object (a useEffect, a memoized child, ...)
  // sees it as unchanged rather than "new every render."
  return useMemo(
    () => ({ reset, recordMouseMove, recordKeydown, recordInteraction, buildBehavior }),
    [reset, recordMouseMove, recordKeydown, recordInteraction, buildBehavior]
  )
}
