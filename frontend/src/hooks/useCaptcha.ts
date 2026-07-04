import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { captchaApi } from '../api/captchaApi'
import { useArrowMatchAnswer } from './captcha/useArrowMatchAnswer'
import { useFCaptchaAnswer } from './captcha/useFCaptchaAnswer'
import { useMazeAnswer } from './captcha/useMazeAnswer'
import { useTextAnswer } from './captcha/useTextAnswer'
import type { CaptchaChallenge, CaptchaPhase, CaptchaSubmission } from '../types/captcha'

/**
 * Owns a captcha challenge's full lifecycle (fetching, refreshing) and composes the mandatory
 * FCaptcha "I'm not a robot" check with each puzzle module's own answer-state hook — text, maze,
 * and arrow-match — so no single hook owns all four pieces of state directly. The FCaptcha widget
 * is shown first; the in-house puzzle is only revealed when FCaptcha's verdict isn't confident
 * enough on its own.
 */
export function useCaptcha() {
  const [challenge, setChallenge] = useState<CaptchaChallenge | null>(null)
  const [loading, setLoading] = useState(true)
  const [phase, setPhase] = useState<CaptchaPhase>('fcaptcha')
  const [assessing, setAssessing] = useState(false)
  const requestIdRef = useRef(0)

  const { fCaptchaToken, setFCaptchaToken, reset: resetFCaptcha } = useFCaptchaAnswer()
  const { answer, setAnswer, reset: resetText } = useTextAnswer()
  const { mazePath, mazeUnsolvableClaim, addMazeStep, toggleMazeUnsolvableClaim, reset: resetMaze } = useMazeAnswer()
  const { tileRotationClicks, rotateTile, reset: resetArrowMatch } = useArrowMatchAnswer()

  const refresh = useCallback(async () => {
    const requestId = ++requestIdRef.current
    setLoading(true)
    setPhase('fcaptcha')
    setAssessing(false)
    resetFCaptcha()
    resetText()
    resetMaze()
    resetArrowMatch()
    try {
      const { data } = await captchaApi.getChallenge()
      if (requestId !== requestIdRef.current) return
      setChallenge(data)
      if (data.type === 'maze' && data.maze) {
        resetMaze(data.maze.startIndex)
      } else if (data.type === 'tileRotate' && data.tileRotate) {
        resetArrowMatch(data.tileRotate.tiles.length)
      }
    } catch {
      if (requestId !== requestIdRef.current) return
      setChallenge(null)
    } finally {
      if (requestId === requestIdRef.current) setLoading(false)
    }
  }, [resetFCaptcha, resetText, resetMaze, resetArrowMatch])

  const handleFCaptchaToken = useCallback(
    async (token: string) => {
      const requestId = ++requestIdRef.current
      setFCaptchaToken(token)
      setAssessing(true)
      try {
        const { data } = await captchaApi.assessFCaptcha(token)
        if (requestId !== requestIdRef.current) return
        if (data.valid && !data.puzzleRequired) {
          setPhase('fcaptcha-only-ready')
        } else if (data.valid && data.puzzleRequired) {
          setPhase('puzzle')
        } else {
          resetFCaptcha()
          setPhase('fcaptcha')
        }
      } catch {
        if (requestId !== requestIdRef.current) return
        resetFCaptcha()
        setPhase('fcaptcha')
      } finally {
        if (requestId === requestIdRef.current) setAssessing(false)
      }
    },
    [setFCaptchaToken, resetFCaptcha]
  )

  useEffect(() => {
    void refresh()
  }, [refresh])

  const buildSubmission = useCallback((): CaptchaSubmission | null => {
    if (!challenge || !fCaptchaToken) return null
    if (phase === 'fcaptcha' || assessing) return null

    return {
      challengeId: challenge.challengeId,
      fCaptchaToken,
      answer: challenge.type === 'text' ? answer : undefined,
      mazePath: challenge.type === 'maze' && !mazeUnsolvableClaim ? mazePath : undefined,
      mazeUnsolvableClaim: challenge.type === 'maze' ? mazeUnsolvableClaim : undefined,
      tileRotationClicks: challenge.type === 'tileRotate' ? tileRotationClicks : undefined,
    }
  }, [challenge, fCaptchaToken, phase, assessing, answer, mazePath, mazeUnsolvableClaim, tileRotationClicks])

  const canSubmit = useMemo(() => buildSubmission() !== null, [buildSubmission])

  return useMemo(
    () => ({
      challenge,
      loading,
      phase,
      assessing,
      canSubmit,
      fCaptchaToken,
      setFCaptchaToken: handleFCaptchaToken,
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
      phase,
      assessing,
      canSubmit,
      fCaptchaToken,
      handleFCaptchaToken,
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
