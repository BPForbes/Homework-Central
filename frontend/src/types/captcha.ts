export type CaptchaChallengeType = 'text' | 'maze' | 'tileRotate'

export interface MazeChallenge {
  width: number
  height: number
  /** Bitmask per cell: 1=North, 2=East, 4=South, 8=West open passage. Row-major indices. */
  cellWalls: number[]
  startIndex: number
  endIndex: number
}

export interface TileChallenge {
  /** 1, 2, or 3 — number of 90° steps out of alignment; solved when total steps ≡ 0 (mod 4). */
  initialRotationSteps: number
}

export interface TileRotateChallenge {
  tiles: TileChallenge[]
}

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
  tileRotationClicks?: number[]
  behavior: CaptchaBehavior
}
