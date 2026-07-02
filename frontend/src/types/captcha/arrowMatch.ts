export interface TileChallenge {
  /** 0–7 — 45° steps, the tile's starting orientation. */
  initialRotationSteps: number
  /** 0–7 — 45° steps, the orientation this tile must be rotated to match. Not fixed to any one
   * direction — varies per tile and per challenge. */
  targetRotationSteps: number
}

export interface TileRotateChallenge {
  tiles: TileChallenge[]
}
