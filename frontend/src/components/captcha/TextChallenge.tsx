import { useMemo } from 'react'

interface TextChallengeProps {
  content: string
  answer: string
  onAnswerChange: (value: string) => void
  onKeydown: () => void
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
 * The code or expression is rendered as per-character distorted, non-selectable spans, with copy/
 * cut/right-click/drag blocked on it, and paste/drop blocked on the answer field — so it can't
 * just be lifted with Ctrl+C/Ctrl+V.
 */
export function TextChallenge({ content, answer, onAnswerChange, onKeydown, disabled, inputId }: TextChallengeProps) {
  const charStyles = useMemo(() => content.split('').map((_, i) => buildCharStyle(content, i)), [content])

  return (
    <>
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

      <input
        id={inputId}
        type="text"
        className="captcha-input"
        value={answer}
        onChange={(e) => onAnswerChange(e.target.value)}
        onKeyDown={onKeydown}
        onPaste={blockEvent}
        onDrop={blockEvent}
        autoComplete="off"
        disabled={disabled}
        placeholder="Your answer"
      />
    </>
  )
}
