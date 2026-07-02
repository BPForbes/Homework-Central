import { useCallback, useState } from 'react'

/** Answer state for the text puzzle module — no telemetry interaction beyond keydown timing,
 * which the caller records directly via useCaptchaTelemetry's recordKeydown. */
export function useTextAnswer() {
  const [answer, setAnswer] = useState('')

  const reset = useCallback(() => {
    setAnswer('')
  }, [])

  return { answer, setAnswer, reset }
}
