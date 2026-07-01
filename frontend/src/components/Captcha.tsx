import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faRotate } from '@fortawesome/free-solid-svg-icons'

interface CaptchaProps {
  prompt: string | null
  answer: string
  onAnswerChange: (value: string) => void
  onRefresh: () => void
  loading?: boolean
  disabled?: boolean
  inputId?: string
}

/** Reusable text captcha widget — same module backs signup and the dashboard verify button. */
export function Captcha({ prompt, answer, onAnswerChange, onRefresh, loading, disabled, inputId }: CaptchaProps) {
  return (
    <div className="captcha">
      <div className="captcha-prompt">
        {loading ? 'Loading a verification question…' : prompt ?? 'Could not load a verification question.'}
        <button
          type="button"
          className="captcha-refresh"
          onClick={onRefresh}
          disabled={disabled}
          aria-label="Get a new question"
          title="Get a new question"
        >
          <FontAwesomeIcon icon={faRotate} />
        </button>
      </div>
      <input
        id={inputId}
        type="text"
        className="captcha-input"
        value={answer}
        onChange={(e) => onAnswerChange(e.target.value)}
        autoComplete="off"
        disabled={disabled || loading}
        placeholder="Your answer"
      />
    </div>
  )
}
