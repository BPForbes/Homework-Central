/**
 * Shared workload model for site-wide adaptive performance.
 * Modes are derived from registered work + measured frame pressure,
 * never from pathname or route helpers.
 */

export type WorkloadPriority = 'decorative' | 'ambient' | 'interactive'

/** Resource hints a surface may declare when it registers. */
export interface ResourceHints {
  cpu?: boolean
  gpu?: boolean
  network?: boolean
}

/**
 * Lifecycle of a registered surface. `useWorkload` registers as `active`
 * on mount and unregisters on unmount. `idle` keeps the id in the registry
 * without counting as interactive pressure.
 */
export type WorkloadLifecycle = 'active' | 'idle'

export interface Workload {
  id: string
  priority: WorkloadPriority
  hints: ResourceHints
  lifecycle: WorkloadLifecycle
}

export type PerformanceMode = 'NORMAL' | 'FOCUSED' | 'CONSTRAINED' | 'CRITICAL'

/**
 * Pond-facing budget. `density` scales spawn caps (effective cap =
 * rule.cap * density). `renderScale` maps onto the existing DPR caps
 * 1.75 / 1.35 / 1.1 — it is not a CSS transform.
 */
export interface PerformanceBudget {
  mode: PerformanceMode
  density: number
  renderScale: number
}

/** One pond rAF sample. Provider ignores quiet frames unless elapsed is janky. */
export interface FramePressureSample {
  stepMs: number
  drawMs: number
  elapsedMs: number
  didStep: boolean
}
