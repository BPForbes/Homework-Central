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

function phaseLabel(captcha: CaptchaHookState): string {
  if (captcha.loading) return 'Loading a verification challenge…'
  if (!captcha.challenge) return 'Could not load a verification challenge.'
  if (captcha.assessing) return 'Checking your verification…'
  if (captcha.phase === 'fcaptcha-only-ready') return 'Verification check passed — you can submit.'
  if (captcha.phase === 'puzzle') return captcha.challenge.label
  return 'Complete the "I\'m not a robot" check below.'
}

/**
 * Reusable captcha widget — same component backs signup and the dashboard verify button. The
 * FCaptcha "I'm not a robot" checkbox is shown first; the text, maze, or arrow-match puzzle is
 * only revealed when FCaptcha's verdict isn't confident enough on its own.
 */
export function Captcha({ captcha, disabled, inputId }: CaptchaProps) {
  const { challenge, loading, phase, assessing } = captcha
  const showFCaptcha = phase === 'fcaptcha'
  const showPuzzle = phase === 'puzzle'

  return (
    <div className="captcha">
      <div className="captcha-label-row">
        <span className="captcha-label">{phaseLabel(captcha)}</span>
        <button
          type="button"
          className="captcha-refresh"
          onClick={() => void captcha.refresh()}
          disabled={disabled || loading || assessing}
          aria-label="Get a new challenge"
          title="Get a new challenge"
        >
          <FontAwesomeIcon icon={faRotate} />
        </button>
      </div>

      {showFCaptcha && challenge && (
        <FCaptchaWidget
          key={challenge.challengeId}
          siteKey={challenge.fCaptchaSiteKey}
          publicUrl={challenge.fCaptchaPublicUrl}
          onToken={captcha.setFCaptchaToken}
          disabled={disabled || loading || assessing}
        />
      )}

      {phase === 'fcaptcha-only-ready' && (
        <p className="captcha-puzzle-status">The &ldquo;I&apos;m not a robot&rdquo; check passed — no puzzle needed.</p>
      )}

      {showPuzzle && challenge?.type === 'text' && challenge.content && (
        <TextChallenge
          inputId={inputId}
          content={challenge.content}
          answer={captcha.answer}
          onAnswerChange={captcha.setAnswer}
          disabled={disabled || loading}
        />
      )}

      {showPuzzle && challenge?.type === 'maze' && challenge.maze && (
        <MazePuzzle
          maze={challenge.maze}
          path={captcha.mazePath}
          onStep={captcha.addMazeStep}
          unsolvableClaim={captcha.mazeUnsolvableClaim}
          onToggleUnsolvableClaim={captcha.toggleMazeUnsolvableClaim}
          disabled={disabled || loading}
        />
      )}

      {showPuzzle && challenge?.type === 'tileRotate' && challenge.tileRotate && (
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
