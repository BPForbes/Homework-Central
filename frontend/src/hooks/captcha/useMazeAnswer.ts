import { useCallback, useState } from 'react'

/** Answer state for the maze puzzle module: the traced path, plus the "no path exists" claim for
 * deliberately-unsolvable mazes. `recordInteraction` is the shared telemetry hook's counter. */
export function useMazeAnswer(recordInteraction: () => void) {
  const [mazePath, setMazePath] = useState<number[]>([])
  const [mazeUnsolvableClaim, setMazeUnsolvableClaim] = useState(false)

  const reset = useCallback((startIndex?: number) => {
    setMazePath(startIndex === undefined ? [] : [startIndex])
    setMazeUnsolvableClaim(false)
  }, [])

  const addMazeStep = useCallback(
    (cellIndex: number) => {
      setMazePath((prev) => [...prev, cellIndex])
      setMazeUnsolvableClaim(false)
      recordInteraction()
    },
    [recordInteraction]
  )

  const toggleMazeUnsolvableClaim = useCallback(() => {
    setMazeUnsolvableClaim((prev) => !prev)
    recordInteraction()
  }, [recordInteraction])

  return { mazePath, mazeUnsolvableClaim, addMazeStep, toggleMazeUnsolvableClaim, reset }
}
