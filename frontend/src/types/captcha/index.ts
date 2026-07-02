import type { MazeChallenge } from './maze'
import type { TileRotateChallenge } from './arrowMatch'

export type { MazeChallenge } from './maze'
export type { TileChallenge, TileRotateChallenge } from './arrowMatch'

export type CaptchaChallengeType = 'text' | 'maze' | 'tileRotate'

export interface CaptchaChallenge {
  challengeId: string
  type: CaptchaChallengeType
  /** Plain instructional text — safe to select/copy. */
  label: string
  /** Text challenges only: the security-relevant code/expression, rendered distorted and
   * non-selectable by TextChallenge so it can't just be copy-pasted into the answer. */
  content?: string
  maze?: MazeChallenge
  tileRotate?: TileRotateChallenge
  /** Always present — the FCaptcha "I'm not a robot" check is a mandatory baseline on every
   * challenge, not one of several alternatives (see useFCaptchaAnswer / FCaptchaWidget). */
  fCaptchaSiteKey: string
  /** Browser-reachable URL to load the FCaptcha widget script from and configure it against. */
  fCaptchaPublicUrl: string
}

export interface CaptchaSubmission {
  challengeId: string
  /** The FCaptcha widget token. Required on every submission. */
  fCaptchaToken?: string
  answer?: string
  mazePath?: number[]
  /** True when the player asserts there's no path from A to B instead of tracing one — some maze
   * challenges are deliberately built as two disconnected regions. Takes precedence over
   * mazePath when set. */
  mazeUnsolvableClaim?: boolean
  tileRotationClicks?: number[]
}
