import { useRef } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faRotate } from '@fortawesome/free-solid-svg-icons'
import type { CaptchaHookState } from '../hooks/useCaptcha'
import { TextChallenge } from './captcha/TextChallenge'
import { MazePuzzle } from './captcha/MazePuzzle'
import { TileRotatePuzzle } from './captcha/TileRotatePuzzle'

interface CaptchaProps {
  captcha: CaptchaHookState
  disabled?: boolean
  inputId?: string
}

/**
 * Reusable captcha widget — same module backs signup and the dashboard verify button. Dispatches
 * to a text/maze/tile-rotate puzzle based on the issued challenge type, and tracks mouse movement
 * across the whole widget for the server-side behavioral score (see useCaptcha).
 */
export function Captcha({ captcha, disabled, inputId }: CaptchaProps) {
  const { challenge, loading, recordMouseMove } = captcha
  const containerRef = useRef<HTMLDivElement>(null)

  function handleMouseMove(e: React.MouseEvent) {
    const rect = containerRef.current?.getBoundingClientRect()
    if (!rect) return
    recordMouseMove(Math.round(e.clientX - rect.left), Math.round(e.clientY - rect.top))
  }

  return (
    <div className="captcha" ref={containerRef} onMouseMove={handleMouseMove}>
      <div className="captcha-label-row">
        <span className="captcha-label">
          {loading ? 'Loading a verification challenge…' : challenge?.label ?? 'Could not load a verification challenge.'}
        </span>
        <button
          type="button"
          className="captcha-refresh"
          onClick={() => void captcha.refresh()}
          disabled={disabled}
          aria-label="Get a new challenge"
          title="Get a new challenge"
        >
          <FontAwesomeIcon icon={faRotate} />
        </button>
      </div>

      {challenge?.type === 'text' && challenge.content && (
        <TextChallenge
          inputId={inputId}
          content={challenge.content}
          answer={captcha.answer}
          onAnswerChange={captcha.setAnswer}
          onKeydown={captcha.recordKeydown}
          disabled={disabled || loading}
        />
      )}

      {challenge?.type === 'maze' && challenge.maze && (
        <MazePuzzle
          maze={challenge.maze}
          path={captcha.mazePath}
          onStep={captcha.addMazeStep}
          unsolvableClaim={captcha.mazeUnsolvableClaim}
          onToggleUnsolvableClaim={captcha.toggleMazeUnsolvableClaim}
          disabled={disabled || loading}
        />
      )}

      {challenge?.type === 'tileRotate' && challenge.tileRotate && (
        <TileRotatePuzzle
          tiles={challenge.tileRotate.tiles}
          rotationClicks={captcha.tileRotationClicks}
          onRotate={captcha.rotateTile}
          disabled={disabled || loading}
        />
      )}
    </div>
  )
}
