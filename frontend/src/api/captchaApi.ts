import api from './authApi'
import type { CaptchaChallenge, CaptchaSubmission, FCaptchaAssessment } from '../types/captcha'

export const captchaApi = {
  getChallenge: () => api.get<CaptchaChallenge>('/captcha/challenge'),

  /** Checks an FCaptcha token and reports whether the fallback puzzle must be shown. */
  assessFCaptcha: (token: string) => api.post<FCaptchaAssessment>('/captcha/assess-fcaptcha', { token }),

  /** Dashboard "Verify" button: strips Guest (if present) and grants VerifiedUser — only if
   * FCaptcha's "I'm not a robot" check passes on its own, or (when it isn't confident enough)
   * the puzzle was also solved correctly. */
  verifyRole: (submission: CaptchaSubmission) => api.post('/captcha/verify-role', submission),
}
