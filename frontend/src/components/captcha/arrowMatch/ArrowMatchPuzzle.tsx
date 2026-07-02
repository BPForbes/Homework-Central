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

/** Each tile shows a faint, non-interactive "target" arrow at a fixed orientation behind a solid
 * arrow the player rotates by clicking. The target varies per tile and per challenge, so the
 * puzzle can't be solved by pattern-memorizing a single fixed direction. */
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
              aria-label={aligned ? `Tile ${i + 1} matched` : `Rotate tile ${i + 1} to match its faint target arrow`}
            >
              <span className="tile-rotate-target" aria-hidden="true">
                <FontAwesomeIcon icon={faLocationArrow} style={{ transform: `rotate(${tile.targetRotationSteps * DEGREES_PER_STEP}deg)` }} />
              </span>
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
        {allAligned ? 'Solved! You can submit now.' : 'Click each arrow to rotate it until it matches the faint target arrow behind it.'}
      </p>
    </div>
  )
}
