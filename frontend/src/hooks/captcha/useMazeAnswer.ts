import { useCallback, useMemo, useState } from 'react'

/** Answer state for the maze puzzle module: the traced path, plus the "no path exists" claim for
 * deliberately-unsolvable mazes. */
export function useMazeAnswer() {
  const [mazePath, setMazePath] = useState<number[]>([])
  const [mazeUnsolvableClaim, setMazeUnsolvableClaim] = useState(false)

  const reset = useCallback((startIndex?: number) => {
    setMazePath(startIndex === undefined ? [] : [startIndex])
    setMazeUnsolvableClaim(false)
  }, [])

  const addMazeStep = useCallback((cellIndex: number) => {
    setMazePath((prev) => [...prev, cellIndex])
    setMazeUnsolvableClaim(false)
  }, [])

  const toggleMazeUnsolvableClaim = useCallback(() => {
    setMazeUnsolvableClaim((prev) => !prev)
  }, [])

  // Identity only changes when mazePath/mazeUnsolvableClaim actually change.
  return useMemo(
    () => ({ mazePath, mazeUnsolvableClaim, addMazeStep, toggleMazeUnsolvableClaim, reset }),
    [mazePath, mazeUnsolvableClaim, addMazeStep, toggleMazeUnsolvableClaim, reset]
  )
}
