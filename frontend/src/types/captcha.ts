export interface CaptchaChallenge {
  challengeId: string
  /** Plain instructional text — safe to select/copy. */
  label: string
  /** The security-relevant part (code to retype, or expression to solve) — rendered distorted
   * and non-selectable by the Captcha component so it can't just be copy-pasted into the answer. */
  content: string
}
