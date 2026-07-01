import { useCallback, useEffect, useState } from 'react'
import { captchaApi } from '../api/captchaApi'

/** Shared captcha state for the signup form and the dashboard "Verify" button. */
export function useCaptcha() {
  const [challengeId, setChallengeId] = useState<string | null>(null)
  const [label, setLabel] = useState<string | null>(null)
  const [content, setContent] = useState<string | null>(null)
  const [answer, setAnswer] = useState('')
  const [loading, setLoading] = useState(true)

  const refresh = useCallback(async () => {
    setLoading(true)
    setAnswer('')
    try {
      const { data } = await captchaApi.getChallenge()
      setChallengeId(data.challengeId)
      setLabel(data.label)
      setContent(data.content)
    } catch {
      setChallengeId(null)
      setLabel(null)
      setContent(null)
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void refresh()
  }, [refresh])

  return { challengeId, label, content, answer, setAnswer, refresh, loading }
}
