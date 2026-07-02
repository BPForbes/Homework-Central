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
}

export interface MouseSample {
  x: number
  y: number
  /** Milliseconds since the challenge was shown. */
  tMs: number
}

/** Raw behavioral telemetry — the client never computes a score itself (a bot could just fabricate
 * a client-computed score), only collects and reports the raw signals for the server to score. */
export interface CaptchaBehavior {
  mouseSamples: MouseSample[]
  keyIntervalsMs: number[]
  totalDurationMs: number
  webdriverFlag: boolean
  interactionCount: number
}

export interface CaptchaSubmission {
  challengeId: string
  answer?: string
  mazePath?: number[]
  /** True when the player asserts there's no path from A to B instead of tracing one — some maze
   * challenges are deliberately built as two disconnected regions. Takes precedence over
   * mazePath when set. */
  mazeUnsolvableClaim?: boolean
  tileRotationClicks?: number[]
  behavior: CaptchaBehavior
}
