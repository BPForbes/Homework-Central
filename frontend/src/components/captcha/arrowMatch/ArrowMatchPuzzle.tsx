import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faLocationArrow } from '@fortawesome/free-solid-svg-icons'
import type { TileChallenge } from '../../../types/captcha'

interface ArrowMatchPuzzleProps {
  tiles: TileChallenge[]
  rotationClicks: number[]
  onRotate: (index: number, clicks: number) => void
  disabled?: boolean
}

const ROTATION_POSITIONS = 8
const DEGREES_PER_STEP = 360 / ROTATION_POSITIONS

/** Click each arrow to rotate it; all arrows must end up pointing the same way (a hidden
 * per-challenge target). The server validates alignment — no on-screen target is shown. */
export function ArrowMatchPuzzle({ tiles, rotationClicks, onRotate, disabled }: ArrowMatchPuzzleProps) {
  const steps = tiles.map((tile, i) => (tile.initialRotationSteps + (rotationClicks[i] ?? 0)) % ROTATION_POSITIONS)
  const allAligned = tiles.every((tile, i) => steps[i] === tile.targetRotationSteps)

  return (
    <div className="tile-rotate-puzzle">
      <div className="tile-rotate-grid">
        {tiles.map((tile, i) => {
          const aligned = steps[i] === tile.targetRotationSteps
          return (
            <button
              key={i}
              type="button"
              className={`tile-rotate-button ${aligned ? 'aligned' : ''}`}
              onClick={() => onRotate(i, (rotationClicks[i] ?? 0) + 1)}
              disabled={disabled}
              aria-label={aligned ? `Tile ${i + 1} aligned` : `Rotate tile ${i + 1}`}
            >
              <FontAwesomeIcon
                icon={faLocationArrow}
                className="tile-rotate-current"
                style={{ transform: `rotate(${steps[i] * DEGREES_PER_STEP}deg)` }}
              />
            </button>
          )
        })}
      </div>
      <p className="captcha-puzzle-status">
        {allAligned ? 'Solved! You can submit now.' : 'Click each arrow to rotate it until every arrow is aligned.'}
      </p>
    </div>
  )
}
