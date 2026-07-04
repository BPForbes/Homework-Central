import { useCallback, useMemo, useState } from 'react'

/** Token state for the mandatory FCaptcha "I'm not a robot" check — set once the widget calls back
 * with a completed token (see FCaptchaWidget). */
export function useFCaptchaAnswer() {
  const [fCaptchaToken, setFCaptchaToken] = useState<string | undefined>(undefined)

  const reset = useCallback(() => {
    setFCaptchaToken(undefined)
  }, [])

  return useMemo(() => ({ fCaptchaToken, setFCaptchaToken, reset }), [fCaptchaToken, setFCaptchaToken, reset])
}
