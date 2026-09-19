import { createContext, useCallback, useMemo, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { budgetFromMode, modeFromPressure } from './modeFromPressure'
import type {
  FramePressureSample,
  PerformanceBudget,
  PerformanceMode,
  Workload,
} from './types'

interface PerformanceContextValue {
  budget: PerformanceBudget
  registerWorkload: (workload: Workload) => void
  unregisterWorkload: (id: string) => void
  reportFrameSample: (sample: FramePressureSample) => void
}

export const PerformanceContext = createContext<PerformanceContextValue | undefined>(undefined)

function emptySample(): FramePressureSample {
  return { stepMs: 0, drawMs: 0, elapsedMs: 0, didStep: false }
}

export function PerformanceProvider({ children }: { children: ReactNode }) {
  const registryRef = useRef(new Map<string, Workload>())
  const modeRef = useRef<PerformanceMode>('NORMAL')
  const pressureStreakRef = useRef(0)
  const recoverStreakRef = useRef(0)
  const [mode, setMode] = useState<PerformanceMode>('NORMAL')

  const applyDerivation = useCallback((sample: FramePressureSample) => {
    const next = modeFromPressure({
      workloads: [...registryRef.current.values()],
      sample,
      currentMode: modeRef.current,
      pressureStreak: pressureStreakRef.current,
      recoverStreak: recoverStreakRef.current,
    })
    pressureStreakRef.current = next.pressureStreak
    recoverStreakRef.current = next.recoverStreak
    if (next.mode !== modeRef.current) {
      modeRef.current = next.mode
      setMode(next.mode)
    }
  }, [])

  const registerWorkload = useCallback((workload: Workload) => {
    registryRef.current.set(workload.id, workload)
    // Quiet sample: apply the interactive floor without replaying the last rAF.
    applyDerivation(emptySample())
  }, [applyDerivation])

  const unregisterWorkload = useCallback((id: string) => {
    registryRef.current.delete(id)
    applyDerivation(emptySample())
  }, [applyDerivation])

  const reportFrameSample = useCallback((sample: FramePressureSample) => {
    applyDerivation(sample)
  }, [applyDerivation])

  const budget = useMemo(() => budgetFromMode(mode), [mode])

  const value = useMemo(
    () => ({ budget, registerWorkload, unregisterWorkload, reportFrameSample }),
    [budget, registerWorkload, unregisterWorkload, reportFrameSample],
  )

  return (
    <PerformanceContext.Provider value={value}>
      {children}
    </PerformanceContext.Provider>
  )
}
