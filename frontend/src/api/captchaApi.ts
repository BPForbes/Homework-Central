import api from './authApi'
import type { CaptchaChallenge, CaptchaSubmission } from '../types/captcha'

export const captchaApi = {
  getChallenge: () => api.get<CaptchaChallenge>('/captcha/challenge'),

  /** Dashboard "Verify" button: strips Guest (if present) and grants VerifiedUser — only if the
   * puzzle was solved correctly AND the behavioral score clears the passing threshold. */
  verifyRole: (submission: CaptchaSubmission) => api.post('/captcha/verify-role', submission),
}
