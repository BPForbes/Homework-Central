import { useCallback, useRef } from 'react'
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

  return { reset, recordMouseMove, recordKeydown, recordInteraction, buildBehavior }
}
