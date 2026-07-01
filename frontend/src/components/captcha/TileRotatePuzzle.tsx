import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faLocationArrow } from '@fortawesome/free-solid-svg-icons'
import type { TileChallenge } from '../../types/captcha'

interface TileRotatePuzzleProps {
  tiles: TileChallenge[]
  rotationClicks: number[]
  onRotate: (index: number, clicks: number) => void
  disabled?: boolean
}

export function TileRotatePuzzle({ tiles, rotationClicks, onRotate, disabled }: TileRotatePuzzleProps) {
  const steps = tiles.map((tile, i) => (tile.initialRotationSteps + (rotationClicks[i] ?? 0)) % 4)
  const allAligned = steps.every((s) => s === 0)

  return (
    <div className="tile-rotate-puzzle">
      <div className="tile-rotate-grid">
        {tiles.map((_, i) => {
          const aligned = steps[i] === 0
          return (
            <button
              key={i}
              type="button"
              className={`tile-rotate-button ${aligned ? 'aligned' : ''}`}
              onClick={() => onRotate(i, (rotationClicks[i] ?? 0) + 1)}
              disabled={disabled}
              aria-label={aligned ? `Tile ${i + 1} aligned` : `Rotate tile ${i + 1}`}
            >
              <FontAwesomeIcon icon={faLocationArrow} style={{ transform: `rotate(${steps[i] * 90}deg)` }} />
            </button>
          )
        })}
      </div>
      <p className="captcha-puzzle-status">
        {allAligned ? 'Solved! You can submit now.' : "Click each tile to rotate it until it's aligned (highlighted)."}
      </p>
    </div>
  )
}
