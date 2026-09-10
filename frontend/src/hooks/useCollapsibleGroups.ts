import { useCallback, useEffect, useMemo, useState } from 'react'

export interface CollapsibleGroups {
  isExpanded: (key: string) => boolean
  toggle: (key: string) => void
  expand: (key: string) => void
}

/**
 * Collapse/expand groups keyed by a stable id (e.g. `category.key`), never by array index.
 * Unknown keys stay expanded: only collapsed keys are stored, so toggling one group
 * cannot change another.
 */
export function useCollapsibleGroups(keys: string[]): CollapsibleGroups {
  const [collapsedKeys, setCollapsedKeys] = useState<ReadonlySet<string>>(() => new Set())
  const knownKeySet = useMemo(() => new Set(keys), [keys])

  useEffect(() => {
    if (knownKeySet.size === 0) {
      return
    }
    setCollapsedKeys((previous) => {
      let changed = false
      const next = new Set<string>()
      for (const key of previous) {
        if (knownKeySet.has(key)) {
          next.add(key)
        } else {
          changed = true
        }
      }
      return changed ? next : previous
    })
  }, [knownKeySet])

  const isExpanded = useCallback(
    (key: string) => !collapsedKeys.has(key),
    [collapsedKeys],
  )

  const toggle = useCallback((key: string) => {
    setCollapsedKeys((previous) => {
      const next = new Set(previous)
      if (next.has(key)) {
        next.delete(key)
      } else {
        next.add(key)
      }
      return next
    })
  }, [])

  const expand = useCallback((key: string) => {
    setCollapsedKeys((previous) => {
      if (!previous.has(key)) {
        return previous
      }
      const next = new Set(previous)
      next.delete(key)
      return next
    })
  }, [])

  return { isExpanded, toggle, expand }
}
