import { useCallback, useEffect, useMemo, useState } from 'react'
import { captchaApi } from '../api/captchaApi'
import { useArrowMatchAnswer } from './captcha/useArrowMatchAnswer'
import { useFCaptchaAnswer } from './captcha/useFCaptchaAnswer'
import { useMazeAnswer } from './captcha/useMazeAnswer'
import { useTextAnswer } from './captcha/useTextAnswer'
import type { CaptchaChallenge, CaptchaSubmission } from '../types/captcha'

/**
 * Owns a captcha challenge's full lifecycle (fetching, refreshing) and composes the mandatory
 * FCaptcha "I'm not a robot" check with each puzzle module's own answer-state hook — text, maze,
 * and arrow-match — so no single hook owns all four pieces of state directly. This hook only wires
 * them together and knows how to build the final submission for whichever challenge is active.
 */
export function useCaptcha() {
  const [challenge, setChallenge] = useState<CaptchaChallenge | null>(null)
  const [loading, setLoading] = useState(true)

  const { fCaptchaToken, setFCaptchaToken, reset: resetFCaptcha } = useFCaptchaAnswer()
  const { answer, setAnswer, reset: resetText } = useTextAnswer()
  const { mazePath, mazeUnsolvableClaim, addMazeStep, toggleMazeUnsolvableClaim, reset: resetMaze } = useMazeAnswer()
  const { tileRotationClicks, rotateTile, reset: resetArrowMatch } = useArrowMatchAnswer()

  const refresh = useCallback(async () => {
    setLoading(true)
    resetFCaptcha()
    resetText()
    resetMaze()
    resetArrowMatch()
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
  }, [resetFCaptcha, resetText, resetMaze, resetArrowMatch])

  useEffect(() => {
    void refresh()
  }, [refresh])

  const buildSubmission = useCallback((): CaptchaSubmission | null => {
    if (!challenge) return null

    return {
      challengeId: challenge.challengeId,
      fCaptchaToken,
      answer: challenge.type === 'text' ? answer : undefined,
      mazePath: challenge.type === 'maze' && !mazeUnsolvableClaim ? mazePath : undefined,
      mazeUnsolvableClaim: challenge.type === 'maze' ? mazeUnsolvableClaim : undefined,
      tileRotationClicks: challenge.type === 'tileRotate' ? tileRotationClicks : undefined,
    }
  }, [challenge, fCaptchaToken, answer, mazePath, mazeUnsolvableClaim, tileRotationClicks])

  // Cached (via useMemo) so the object identity itself only changes when one of these values
  // actually does — any consumer that depends on the whole `captcha` object (a useEffect, a
  // memoized child component, ...) sees a stable reference across renders where nothing relevant
  // changed, rather than treating every render as a change.
  return useMemo(
    () => ({
      challenge,
      loading,
      fCaptchaToken,
      setFCaptchaToken,
      answer,
      setAnswer,
      mazePath,
      addMazeStep,
      mazeUnsolvableClaim,
      toggleMazeUnsolvableClaim,
      tileRotationClicks,
      rotateTile,
      buildSubmission,
      refresh,
    }),
    [
      challenge,
      loading,
      fCaptchaToken,
      setFCaptchaToken,
      answer,
      setAnswer,
      mazePath,
      addMazeStep,
      mazeUnsolvableClaim,
      toggleMazeUnsolvableClaim,
      tileRotationClicks,
      rotateTile,
      buildSubmission,
      refresh,
    ]
  )
}

export type CaptchaHookState = ReturnType<typeof useCaptcha>
