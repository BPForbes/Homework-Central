import { useContext } from 'react'
import { PerformanceContext } from './PerformanceContext'
import type { FramePressureSample, PerformanceBudget } from './types'

export function usePerformanceBudget(): PerformanceBudget {
  const ctx = useContext(PerformanceContext)
  if (!ctx) {
    throw new Error('usePerformanceBudget must be used within a PerformanceProvider')
  }
  return ctx.budget
}

export function useReportFrameSample(): (sample: FramePressureSample) => void {
  const ctx = useContext(PerformanceContext)
  if (!ctx) {
    throw new Error('useReportFrameSample must be used within a PerformanceProvider')
  }
  return ctx.reportFrameSample
}
