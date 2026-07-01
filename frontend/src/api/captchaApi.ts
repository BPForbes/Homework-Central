import api from './authApi'
import type { CaptchaChallenge } from '../types/captcha'

export const captchaApi = {
  getChallenge: () => api.get<CaptchaChallenge>('/captcha/challenge'),

  /** Dashboard "Verify" button: strips Guest (if present) and grants VerifiedUser. */
  verifyRole: (challengeId: string, answer: string) =>
    api.post('/captcha/verify-role', { challengeId, answer }),
}
