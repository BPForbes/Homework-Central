import { useCallback, useEffect, useRef, useState } from 'react'
import { captchaApi } from '../api/captchaApi'
import type { CaptchaChallenge, CaptchaSubmission, MouseSample } from '../types/captcha'

const MAX_MOUSE_SAMPLES = 400
const MOUSE_SAMPLE_INTERVAL_MS = 60

/**
 * Owns a captcha challenge's full lifecycle: fetching, puzzle-specific answer state (text/maze/
 * tile-rotate), and the raw behavioral telemetry (mouse movement, keystroke timing, interaction
 * count, duration, navigator.webdriver) collected while it's being solved. The score itself is
 * never computed here — only collected and sent for the server to score, since a client-computed
 * score could simply be fabricated by a bot.
 */
export function useCaptcha() {
  const [challenge, setChallenge] = useState<CaptchaChallenge | null>(null)
  const [loading, setLoading] = useState(true)
  const [answer, setAnswer] = useState('')
  const [mazePath, setMazePath] = useState<number[]>([])
  const [mazeUnsolvableClaim, setMazeUnsolvableClaim] = useState(false)
  const [tileRotationClicks, setTileRotationClicks] = useState<number[]>([])

  const startedAtRef = useRef(0)
  const mouseSamplesRef = useRef<MouseSample[]>([])
  const lastSampleAtRef = useRef(0)
  const keyTimestampsRef = useRef<number[]>([])
  const interactionCountRef = useRef(0)
  const webdriverFlagRef = useRef(false)

  const resetTelemetry = useCallback(() => {
    startedAtRef.current = performance.now()
    mouseSamplesRef.current = []
    lastSampleAtRef.current = 0
    keyTimestampsRef.current = []
    interactionCountRef.current = 0
    webdriverFlagRef.current =
      typeof navigator !== 'undefined' && Boolean((navigator as { webdriver?: boolean }).webdriver)
  }, [])

  const refresh = useCallback(async () => {
    setLoading(true)
    setAnswer('')
    setMazePath([])
    setMazeUnsolvableClaim(false)
    setTileRotationClicks([])
    resetTelemetry()
    try {
      const { data } = await captchaApi.getChallenge()
      setChallenge(data)
      if (data.type === 'maze' && data.maze) {
        setMazePath([data.maze.startIndex])
      } else if (data.type === 'tileRotate' && data.tileRotate) {
        setTileRotationClicks(new Array(data.tileRotate.tiles.length).fill(0))
      }
    } catch {
      setChallenge(null)
    } finally {
      setLoading(false)
    }
  }, [resetTelemetry])

  useEffect(() => {
    void refresh()
  }, [refresh])

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

  const addMazeStep = useCallback(
    (cellIndex: number) => {
      setMazePath((prev) => [...prev, cellIndex])
      setMazeUnsolvableClaim(false)
      recordInteraction()
    },
    [recordInteraction]
  )

  const toggleMazeUnsolvableClaim = useCallback(() => {
    setMazeUnsolvableClaim((prev) => !prev)
    recordInteraction()
  }, [recordInteraction])

  const rotateTile = useCallback(
    (index: number, clicks: number) => {
      setTileRotationClicks((prev) => {
        const next = [...prev]
        next[index] = clicks
        return next
      })
      recordInteraction()
    },
    [recordInteraction]
  )

  const buildSubmission = useCallback((): CaptchaSubmission | null => {
    if (!challenge) return null

    const timestamps = keyTimestampsRef.current
    const keyIntervalsMs = timestamps.slice(1).map((t, i) => Math.round(t - timestamps[i]))

    return {
      challengeId: challenge.challengeId,
      answer: challenge.type === 'text' ? answer : undefined,
      mazePath: challenge.type === 'maze' && !mazeUnsolvableClaim ? mazePath : undefined,
      mazeUnsolvableClaim: challenge.type === 'maze' ? mazeUnsolvableClaim : undefined,
      tileRotationClicks: challenge.type === 'tileRotate' ? tileRotationClicks : undefined,
      behavior: {
        mouseSamples: mouseSamplesRef.current,
        keyIntervalsMs,
        totalDurationMs: Math.round(performance.now() - startedAtRef.current),
        webdriverFlag: webdriverFlagRef.current,
        interactionCount: interactionCountRef.current,
      },
    }
  }, [challenge, answer, mazePath, mazeUnsolvableClaim, tileRotationClicks])

  return {
    challenge,
    loading,
    answer,
    setAnswer,
    mazePath,
    addMazeStep,
    mazeUnsolvableClaim,
    toggleMazeUnsolvableClaim,
    tileRotationClicks,
    rotateTile,
    recordMouseMove,
    recordKeydown,
    buildSubmission,
    refresh,
  }
}

export type CaptchaHookState = ReturnType<typeof useCaptcha>
