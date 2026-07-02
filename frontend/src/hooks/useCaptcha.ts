import { useCallback, useEffect, useMemo, useState } from 'react'
import { captchaApi } from '../api/captchaApi'
import { useArrowMatchAnswer } from './captcha/useArrowMatchAnswer'
import { useCaptchaTelemetry } from './captcha/useCaptchaTelemetry'
import { useMazeAnswer } from './captcha/useMazeAnswer'
import { useTextAnswer } from './captcha/useTextAnswer'
import type { CaptchaChallenge, CaptchaSubmission } from '../types/captcha'

/**
 * Owns a captcha challenge's full lifecycle (fetching, refreshing) and composes the shared
 * telemetry hook with each puzzle module's own answer-state hook — text, maze, and arrow-match —
 * so no single hook owns all three puzzle types' state directly. The puzzle modules live under
 * captcha/ and components/captcha/*; this hook only wires them together and knows how to build
 * the final submission for whichever challenge type is currently active.
 */
export function useCaptcha() {
  const [challenge, setChallenge] = useState<CaptchaChallenge | null>(null)
  const [loading, setLoading] = useState(true)

  const { reset: resetTelemetry, recordMouseMove, recordKeydown, recordInteraction, buildBehavior } = useCaptchaTelemetry()
  const { answer, setAnswer, reset: resetText } = useTextAnswer()
  const { mazePath, mazeUnsolvableClaim, addMazeStep, toggleMazeUnsolvableClaim, reset: resetMaze } = useMazeAnswer(recordInteraction)
  const { tileRotationClicks, rotateTile, reset: resetArrowMatch } = useArrowMatchAnswer(recordInteraction)

  const refresh = useCallback(async () => {
    setLoading(true)
    resetText()
    resetMaze()
    resetArrowMatch()
    resetTelemetry()
    try {
      const { data } = await captchaApi.getChallenge()
      setChallenge(data)
      if (data.type === 'maze' && data.maze) {
        resetMaze(data.maze.startIndex)
      } else if (data.type === 'tileRotate' && data.tileRotate) {
        resetArrowMatch(data.tileRotate.tiles.length)
      }
    } catch {
      setChallenge(null)
    } finally {
      setLoading(false)
    }
  }, [resetText, resetMaze, resetArrowMatch, resetTelemetry])

  useEffect(() => {
    void refresh()
  }, [refresh])

  const buildSubmission = useCallback((): CaptchaSubmission | null => {
    if (!challenge) return null

    return {
      challengeId: challenge.challengeId,
      answer: challenge.type === 'text' ? answer : undefined,
      mazePath: challenge.type === 'maze' && !mazeUnsolvableClaim ? mazePath : undefined,
      mazeUnsolvableClaim: challenge.type === 'maze' ? mazeUnsolvableClaim : undefined,
      tileRotationClicks: challenge.type === 'tileRotate' ? tileRotationClicks : undefined,
      behavior: buildBehavior(),
    }
  }, [challenge, answer, mazePath, mazeUnsolvableClaim, tileRotationClicks, buildBehavior])

  // Cached (via useMemo) so the object identity itself only changes when one of these values
  // actually does — any consumer that depends on the whole `captcha` object (a useEffect, a
  // memoized child component, ...) sees a stable reference across renders where nothing relevant
  // changed, rather than treating every render as a change.
  return useMemo(
    () => ({
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
    }),
    [
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
    ]
  )
}

export type CaptchaHookState = ReturnType<typeof useCaptcha>
