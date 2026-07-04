import { useCallback, useMemo, useState } from 'react'

/** Answer state for the arrow-match puzzle module: rotation click counts per tile. */
export function useArrowMatchAnswer() {
  const [tileRotationClicks, setTileRotationClicks] = useState<number[]>([])

  const reset = useCallback((tileCount?: number) => {
    setTileRotationClicks(tileCount === undefined ? [] : new Array(tileCount).fill(0))
  }, [])

  const rotateTile = useCallback((index: number, clicks: number) => {
    setTileRotationClicks((prev) => {
      const next = [...prev]
      next[index] = clicks
      return next
    })
  }, [])

  // Identity only changes when tileRotationClicks itself changes.
  return useMemo(() => ({ tileRotationClicks, rotateTile, reset }), [tileRotationClicks, rotateTile, reset])
}
