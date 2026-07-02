export interface MazeChallenge {
  width: number
  height: number
  /** Bitmask per cell: 1=North, 2=East, 4=South, 8=West open passage. Row-major indices. */
  cellWalls: number[]
  startIndex: number
  endIndex: number
}
