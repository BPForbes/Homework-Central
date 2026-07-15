export const SCENE_FPS = 30
export const SCENE_FRAME_MS = 1000 / SCENE_FPS

export function secondsToFrames(seconds: number): number {
  return Math.max(1, Math.round(seconds * SCENE_FPS))
}

export function sampleFrameRange(minSeconds: number, maxSeconds: number): number {
  return secondsToFrames(minSeconds + Math.random() * (maxSeconds - minSeconds))
}

interface AmbientSpawnerConfig {
  minimumIntervalSeconds: number
  baseRatePerSecond: number
  maximumBurst: number
}

/**
 * Blue-noise-style event scheduling with density feedback. The minimum interval
 * prevents visible clustering; the exponential tail preserves natural
 * irregularity; density feedback slows arrivals as the scene approaches its
 * entity cap.
 */
export class AmbientSpawner {
  private framesUntilEvent = 1

  constructor(private readonly config: AmbientSpawnerConfig) {}

  update(currentCount: number, maximumCount: number): number {
    if (currentCount >= maximumCount) {
      this.framesUntilEvent = Math.max(this.framesUntilEvent, SCENE_FPS)
      return 0
    }

    this.framesUntilEvent--
    if (this.framesUntilEvent > 0)
      return 0

    this.framesUntilEvent = this.sampleNextInterval(currentCount, maximumCount)
    return Math.min(this.sampleBurstSize(), maximumCount - currentCount)
  }

  reset(currentCount = 0, maximumCount = 1): void {
    this.framesUntilEvent = this.sampleNextInterval(currentCount, maximumCount)
  }

  private sampleNextInterval(currentCount: number, maximumCount: number): number {
    const availableFraction = Math.max(0, 1 - currentCount / maximumCount)
    const effectiveRate = this.config.baseRatePerSecond * availableFraction * availableFraction
    if (effectiveRate <= 0)
      return Number.MAX_SAFE_INTEGER

    const random = Math.max(Math.random(), Number.EPSILON)
    const exponentialExtraSeconds = -Math.log(random) / effectiveRate
    return secondsToFrames(this.config.minimumIntervalSeconds + exponentialExtraSeconds)
  }

  private sampleBurstSize(): number {
    if (this.config.maximumBurst <= 1)
      return 1

    const random = Math.random()
    const sampled = random < 0.55 ? 1 : random < 0.82 ? 2 : random < 0.95 ? 3 : 4
    return Math.min(sampled, this.config.maximumBurst)
  }
}

export interface ScheduledSpawn<TKind extends string> {
  kind: TKind
  framesRemaining: number
  anchorX?: number
  anchorY?: number
  heading?: number
}

export function scheduleBurst<TKind extends string>(
  queue: ScheduledSpawn<TKind>[],
  kind: TKind,
  size: number,
  anchor?: { x: number; y: number; heading?: number },
): void {
  for (let index = 0; index < size; index++) {
    queue.push({
      kind,
      framesRemaining: index === 0 ? 0 : index * Math.floor(3 + Math.random() * 8),
      anchorX: anchor?.x,
      anchorY: anchor?.y,
      heading: anchor?.heading,
    })
  }
}
