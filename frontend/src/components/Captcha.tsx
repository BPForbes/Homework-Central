import { useMemo } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faRotate } from '@fortawesome/free-solid-svg-icons'

interface CaptchaProps {
  label: string | null
  content: string | null
  answer: string
  onAnswerChange: (value: string) => void
  onRefresh: () => void
  loading?: boolean
  disabled?: boolean
  inputId?: string
}

const CHAR_COLORS = ['#1e3a8a', '#1d4ed8', '#2563eb', '#3730a3', '#312e81']

/** Deterministic per-character jitter (not Math.random) so re-renders that don't change the
 * content — e.g. typing into the answer field — don't make the distorted characters visibly
 * shuffle around. */
function buildCharStyle(content: string, index: number): React.CSSProperties {
  const seed = content.length * 31 + index * 17 + content.charCodeAt(index)
  const rotate = (seed % 25) - 12
  const translateY = (seed % 7) - 3
  const color = CHAR_COLORS[seed % CHAR_COLORS.length]
  return { transform: `rotate(${rotate}deg) translateY(${translateY}px)`, color }
}

function blockEvent(e: React.SyntheticEvent) {
  e.preventDefault()
}

/**
 * Reusable text captcha widget — same module backs signup and the dashboard verify button.
 * `label` is plain readable text; `content` (the code or expression to solve) is rendered as
 * per-character distorted, non-selectable spans, and copy/cut/right-click/drag are blocked on it,
 * with paste/drop blocked on the answer field — so it can't just be lifted with Ctrl+C/Ctrl+V.
 */
export function Captcha({
  label,
  content,
  answer,
  onAnswerChange,
  onRefresh,
  loading,
  disabled,
  inputId,
}: CaptchaProps) {
  const charStyles = useMemo(
    () => (content ?? '').split('').map((_, i) => buildCharStyle(content ?? '', i)),
    [content]
  )

  return (
    <div className="captcha">
      <div className="captcha-label-row">
        <span className="captcha-label">
          {loading ? 'Loading a verification question…' : label ?? 'Could not load a verification question.'}
        </span>
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

      {content && (
        <div
          className="captcha-content"
          aria-label={content}
          onCopy={blockEvent}
          onCut={blockEvent}
          onContextMenu={blockEvent}
          onDragStart={blockEvent}
        >
          {content.split('').map((char, i) => (
            <span key={i} className="captcha-char" style={charStyles[i]} aria-hidden="true">
              {char}
            </span>
          ))}
        </div>
      )}

      <input
        id={inputId}
        type="text"
        className="captcha-input"
        value={answer}
        onChange={(e) => onAnswerChange(e.target.value)}
        onPaste={blockEvent}
        onDrop={blockEvent}
        autoComplete="off"
        disabled={disabled || loading}
        placeholder="Your answer"
      />
    </div>
  )
}
