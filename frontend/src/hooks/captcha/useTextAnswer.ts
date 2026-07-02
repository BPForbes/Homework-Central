import { useCallback, useMemo, useState } from 'react'

/** Answer state for the text puzzle module — no telemetry interaction beyond keydown timing,
 * which the caller records directly via useCaptchaTelemetry's recordKeydown. */
export function useTextAnswer() {
  const [answer, setAnswer] = useState('')

  const reset = useCallback(() => {
    setAnswer('')
  }, [])

  // Identity only changes when `answer` itself changes — setAnswer/reset are already stable.
  return useMemo(() => ({ answer, setAnswer, reset }), [answer, setAnswer, reset])
}
