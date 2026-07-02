import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faRotate } from '@fortawesome/free-solid-svg-icons'
import type { CaptchaHookState } from '../hooks/useCaptcha'
import { FCaptchaWidget } from './captcha/fcaptcha/FCaptchaWidget'
import { TextChallenge } from './captcha/text/TextChallenge'
import { MazePuzzle } from './captcha/maze/MazePuzzle'
import { ArrowMatchPuzzle } from './captcha/arrowMatch/ArrowMatchPuzzle'

interface CaptchaProps {
  captcha: CaptchaHookState
  disabled?: boolean
  inputId?: string
}

/**
 * Reusable captcha widget — same component backs signup and the dashboard verify button. The
 * FCaptcha "I'm not a robot" checkbox is always shown first and is mandatory; the text, maze, or
 * arrow-match puzzle module underneath is only actually required server-side if FCaptcha's own
 * verdict isn't confident enough on its own (see CaptchaService.ValidateAsync), but is always
 * rendered so solving it up front never hurts.
 */
export function Captcha({ captcha, disabled, inputId }: CaptchaProps) {
  const { challenge, loading } = captcha

  return (
    <div className="captcha">
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

      {challenge && (
        <FCaptchaWidget
          key={challenge.challengeId}
          siteKey={challenge.fCaptchaSiteKey}
          publicUrl={challenge.fCaptchaPublicUrl}
          onToken={captcha.setFCaptchaToken}
          disabled={disabled || loading}
        />
      )}

      {challenge?.type === 'text' && challenge.content && (
        <TextChallenge
          inputId={inputId}
          content={challenge.content}
          answer={captcha.answer}
          onAnswerChange={captcha.setAnswer}
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
        <ArrowMatchPuzzle
          tiles={challenge.tileRotate.tiles}
          rotationClicks={captcha.tileRotationClicks}
          onRotate={captcha.rotateTile}
          disabled={disabled || loading}
        />
      )}
    </div>
  )
}
