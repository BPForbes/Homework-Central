import { useContext, useEffect } from 'react'
import { PerformanceContext } from './PerformanceContext'
import type { ResourceHints, WorkloadLifecycle, WorkloadPriority } from './types'

interface UseWorkloadOptions {
  id: string
  priority: WorkloadPriority
  hints?: ResourceHints
  lifecycle?: WorkloadLifecycle
}

/**
 * Register a workload for the lifetime of the calling component.
 * The `id` must be stable across renders. Unregisters on unmount.
 * Do not key `id` off a pathname — registration is the signal.
 */
export function useWorkload(options: UseWorkloadOptions): void {
  const ctx = useContext(PerformanceContext)
  if (!ctx)
    throw new Error('useWorkload must be used within a PerformanceProvider')

  const { registerWorkload, unregisterWorkload } = ctx
  const { id, priority, lifecycle = 'active' } = options
  const cpu = options.hints?.cpu === true
  const gpu = options.hints?.gpu === true
  const network = options.hints?.network === true

  useEffect(() => {
    registerWorkload({
      id,
      priority,
      hints: { cpu, gpu, network },
      lifecycle,
    })
    return () => unregisterWorkload(id)
  }, [id, priority, lifecycle, cpu, gpu, network, registerWorkload, unregisterWorkload])
}
