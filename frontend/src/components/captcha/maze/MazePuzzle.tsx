import type { MazeChallenge } from '../../../types/captcha'

interface MazePuzzleProps {
  maze: MazeChallenge
  path: number[]
  onStep: (cellIndex: number) => void
  unsolvableClaim: boolean
  onToggleUnsolvableClaim: () => void
  disabled?: boolean
}

const NORTH = 1
const EAST = 2
const SOUTH = 4
const WEST = 8

function isOpen(walls: number, dir: number) {
  return (walls & dir) !== 0
}

/** Directional-button navigation (rather than click-anywhere) so it works the same on mouse,
 * touch, and keyboard, and so every recorded step is guaranteed adjacent to the current cell.
 * Some mazes are deliberately built with no path from A to B at all — the "cannot connect" button
 * lets the player assert that instead of hunting forever for a route that doesn't exist. */
export function MazePuzzle({ maze, path, onStep, unsolvableClaim, onToggleUnsolvableClaim, disabled }: MazePuzzleProps) {
  const current = path.length > 0 ? path[path.length - 1] : maze.startIndex
  const solved = current === maze.endIndex

  function tryMove(dx: number, dy: number, dir: number) {
    if (disabled || solved || unsolvableClaim) return
    const x = current % maze.width
    const y = Math.floor(current / maze.width)
    const nx = x + dx
    const ny = y + dy
    if (nx < 0 || nx >= maze.width || ny < 0 || ny >= maze.height) return
    if (!isOpen(maze.cellWalls[current], dir)) return
    onStep(ny * maze.width + nx)
  }

  const controlsDisabled = disabled || solved || unsolvableClaim

  let status = 'Guide the marker from A to B.'
  if (solved) status = 'Solved! You can submit now.'
  else if (unsolvableClaim) status = "You've marked A and B as disconnected. Submit now, or move the marker to keep trying instead."

  return (
    <div className="maze-puzzle">
      <div
        className="maze-grid"
        style={{ gridTemplateColumns: `repeat(${maze.width}, 1fr)` }}
        role="group"
        aria-label={solved ? 'Maze solved' : 'Maze puzzle, use the arrow buttons to navigate'}
      >
        {Array.from({ length: maze.width * maze.height }, (_, index) => {
          const walls = maze.cellWalls[index]
          const isCurrent = index === current
          const isStart = index === maze.startIndex
          const isEnd = index === maze.endIndex
          const visited = path.includes(index)
          const classes = [
            'maze-cell',
            isOpen(walls, NORTH) ? '' : 'wall-n',
            isOpen(walls, EAST) ? '' : 'wall-e',
            isOpen(walls, SOUTH) ? '' : 'wall-s',
            isOpen(walls, WEST) ? '' : 'wall-w',
            visited ? 'visited' : '',
            isCurrent ? 'current' : '',
          ]
            .filter(Boolean)
            .join(' ')

          return (
            <div key={index} className={classes}>
              {isStart && !isCurrent && <span className="maze-marker">A</span>}
              {isEnd && !isCurrent && <span className="maze-marker">B</span>}
              {isCurrent && <span className="maze-token" aria-hidden="true" />}
            </div>
          )
        })}
      </div>
      <div className="maze-controls">
        <button type="button" className="maze-btn" onClick={() => tryMove(0, -1, NORTH)} disabled={controlsDisabled} aria-label="Move up">
          ↑
        </button>
        <div className="maze-controls-row">
          <button type="button" className="maze-btn" onClick={() => tryMove(-1, 0, WEST)} disabled={controlsDisabled} aria-label="Move left">
            ←
          </button>
          <button type="button" className="maze-btn" onClick={() => tryMove(0, 1, SOUTH)} disabled={controlsDisabled} aria-label="Move down">
            ↓
          </button>
          <button type="button" className="maze-btn" onClick={() => tryMove(1, 0, EAST)} disabled={controlsDisabled} aria-label="Move right">
            →
          </button>
        </div>
      </div>
      <button
        type="button"
        className={`maze-unsolvable-btn${unsolvableClaim ? ' active' : ''}`}
        onClick={onToggleUnsolvableClaim}
        disabled={disabled || solved}
        aria-pressed={unsolvableClaim}
      >
        {unsolvableClaim ? '✓ Point A to B cannot connect' : 'Point A to B cannot connect'}
      </button>
      <p className="captcha-puzzle-status">{status}</p>
    </div>
  )
}
