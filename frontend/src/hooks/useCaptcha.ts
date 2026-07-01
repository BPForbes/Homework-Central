import { useCallback, useEffect, useState } from 'react'
import { captchaApi } from '../api/captchaApi'

/** Shared captcha state for the signup form and the dashboard "Verify" button. */
export function useCaptcha() {
  const [challengeId, setChallengeId] = useState<string | null>(null)
  const [prompt, setPrompt] = useState<string | null>(null)
  const [answer, setAnswer] = useState('')
  const [loading, setLoading] = useState(true)

  const refresh = useCallback(async () => {
    setLoading(true)
    setAnswer('')
    try {
      const { data } = await captchaApi.getChallenge()
      setChallengeId(data.challengeId)
      setPrompt(data.prompt)
    } catch {
      setChallengeId(null)
      setPrompt(null)
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void refresh()
  }, [refresh])

  return { challengeId, prompt, answer, setAnswer, refresh, loading }
}
