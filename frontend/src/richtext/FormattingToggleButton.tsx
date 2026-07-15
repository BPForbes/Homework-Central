import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faEye, faEyeSlash } from '@fortawesome/free-solid-svg-icons'

interface FormattingToggleButtonProps {
  active: boolean
  onToggle: () => void
  disabled?: boolean
}

/**
 * "Show formatting" / "Hide formatting" — renders the Markdown+LaTeX in whatever the user is
 * currently typing (a chat message, a mock chat message, or an info entry) so they can see it
 * formatted before sending or saving. Distinct from a channel's Edit/Preview mode: this toggle
 * only concerns the current unsent draft, in both real and mock composers.
 */
export function FormattingToggleButton({ active, onToggle, disabled = false }: FormattingToggleButtonProps) {
  return (
    <button
      type="button"
      className={`rich-preview-toggle ${active ? 'active' : ''}`}
      onClick={onToggle}
      disabled={disabled}
      aria-pressed={active}
      aria-label={active ? 'Hide formatting preview' : 'Show formatting preview'}
    >
      <FontAwesomeIcon icon={active ? faEyeSlash : faEye} aria-hidden="true" />
      {active ? 'Hide formatting' : 'Show formatting'}
    </button>
  )
}
