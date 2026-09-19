import type {
  FramePressureSample,
  PerformanceBudget,
  PerformanceMode,
  Workload,
} from './types'

/**
 * RAIL animation budget: produce a frame in ≤10 ms of JS
 * (16.6 ms vsync minus ~6 ms browser). Not a CSS duration token.
 * @see https://web.dev/articles/rail
 */
export const RAIL_JS_MS = 10

/**
 * RAIL idle chunk / jank threshold. rAF elapsed above this is pressure
 * even when the sim did not step. Not a CSS duration token.
 */
export const JANK_ELAPSED_MS = 50

/**
 * Recovery / escalate streaks — analog of pond DPR hysteresis
 * (≥26 → 1.35, ≥34 → 1.1, recover ≤20 / ≤26). Counts are samples
 * that carried a sim step (or a janky rAF gap), not wall-clock ms.
 */
export const ESCALATE_TO_FOCUSED = 2
export const ESCALATE_TO_CONSTRAINED = 3
export const ESCALATE_TO_CRITICAL = 4
export const RECOVER_FROM_CRITICAL = 4
export const RECOVER_FROM_CONSTRAINED = 3
export const RECOVER_FROM_FOCUSED = 3

/** Existing pond DPR caps. renderScale 1 → 1.75, mid → 1.35, low → 1.1. */
export const RENDER_DPR_HIGH = 1.75
export const RENDER_DPR_MID = 1.35
export const RENDER_DPR_LOW = 1.1

const RENDER_SCALE_HIGH = 1
const RENDER_SCALE_MID = RENDER_DPR_MID / RENDER_DPR_HIGH
const RENDER_SCALE_LOW = RENDER_DPR_LOW / RENDER_DPR_HIGH

export interface ModeFromPressureInput {
  workloads: readonly Workload[]
  sample: FramePressureSample
  currentMode: PerformanceMode
  pressureStreak: number
  recoverStreak: number
}

export interface ModeFromPressureResult {
  mode: PerformanceMode
  pressureStreak: number
  recoverStreak: number
}

function hasInteractiveWork(workloads: readonly Workload[]): boolean {
  for (const workload of workloads) {
    if (workload.lifecycle === 'active' && workload.priority === 'interactive')
      return true
  }
  return false
}

/**
 * A sample is over budget when JS work misses the RAIL 10 ms window
 * or the rAF gap exceeds the idle/jank threshold. Elapsed vs
 * SCENE_FRAME_MS is not used: the pond rAF runs at display refresh
 * (~16 ms) while the sim stays at 30 FPS.
 */
export function sampleIsOverBudget(sample: FramePressureSample): boolean {
  const jsCost = sample.stepMs + sample.drawMs
  if (jsCost > RAIL_JS_MS) return true
  if (sample.elapsedMs > JANK_ELAPSED_MS) return true
  return false
}

/**
 * Quiet frames (no sim step, healthy elapsed) must not count as recovery
 * or the 60 Hz rAF between 30 FPS steps would flap the mode.
 */
export function sampleIsMeaningful(sample: FramePressureSample): boolean {
  return sample.didStep || sample.elapsedMs > JANK_ELAPSED_MS
}

export function budgetFromMode(mode: PerformanceMode): PerformanceBudget {
  switch (mode) {
    case 'NORMAL':
      return { mode, density: 1, renderScale: RENDER_SCALE_HIGH }
    case 'FOCUSED':
      // Shed decoration first; keep high DPR.
      return { mode, density: 0.55, renderScale: RENDER_SCALE_HIGH }
    case 'CONSTRAINED':
      return { mode, density: 0.32, renderScale: RENDER_SCALE_MID }
    case 'CRITICAL':
      return { mode, density: 0.12, renderScale: RENDER_SCALE_LOW }
  }
}

/** Map budget.renderScale onto the existing 1.75 / 1.35 / 1.1 caps. */
export function dprCapFromRenderScale(renderScale: number): number {
  if (renderScale >= 0.9) return RENDER_DPR_HIGH
  if (renderScale >= 0.7) return RENDER_DPR_MID
  return RENDER_DPR_LOW
}

/**
 * Pure: registered work + one frame-pressure sample + hysteresis counters
 * → next mode. No pathname. Recovery steps one rung at a time.
 */
export function modeFromPressure(input: ModeFromPressureInput): ModeFromPressureResult {
  const { workloads, sample, currentMode } = input
  let { pressureStreak, recoverStreak } = input
  const interactive = hasInteractiveWork(workloads)

  if (!sampleIsMeaningful(sample)) {
    const mode = interactive && currentMode === 'NORMAL' ? 'FOCUSED' : currentMode
    return { mode, pressureStreak, recoverStreak }
  }

  const overBudget = sampleIsOverBudget(sample)

  if (overBudget) {
    pressureStreak += 1
    recoverStreak = 0
  } else {
    recoverStreak += 1
    pressureStreak = 0
  }

  let mode = currentMode

  if (overBudget) {
    if (mode === 'NORMAL' && pressureStreak >= ESCALATE_TO_FOCUSED) {
      mode = 'FOCUSED'
      pressureStreak = 0
    } else if (mode === 'FOCUSED' && pressureStreak >= ESCALATE_TO_CONSTRAINED) {
      mode = 'CONSTRAINED'
      pressureStreak = 0
    } else if (mode === 'CONSTRAINED' && pressureStreak >= ESCALATE_TO_CRITICAL) {
      mode = 'CRITICAL'
      pressureStreak = 0
    }
  } else if (mode === 'CRITICAL' && recoverStreak >= RECOVER_FROM_CRITICAL) {
    mode = 'CONSTRAINED'
    recoverStreak = 0
  } else if (mode === 'CONSTRAINED' && recoverStreak >= RECOVER_FROM_CONSTRAINED) {
    mode = 'FOCUSED'
    recoverStreak = 0
  } else if (
    mode === 'FOCUSED' &&
    recoverStreak >= RECOVER_FROM_FOCUSED &&
    !interactive
  ) {
    mode = 'NORMAL'
    recoverStreak = 0
  }

  // Interactive work is a floor, never a route check.
  if (interactive && mode === 'NORMAL')
    mode = 'FOCUSED'

  return { mode, pressureStreak, recoverStreak }
}
