import type { ReactNode } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faChevronDown } from '@fortawesome/free-solid-svg-icons'

interface CollapsibleSidebarSectionProps {
  expanded: boolean
  onToggle: () => void
  label: ReactNode
  controlsId: string
  children?: ReactNode
  className?: string
}

export function CollapsibleSidebarSection({
  expanded,
  onToggle,
  label,
  controlsId,
  children,
  className,
}: CollapsibleSidebarSectionProps) {
  return (
    <section className={className}>
      <button
        type="button"
        className="chat-category-label-row"
        aria-expanded={expanded}
        aria-controls={controlsId}
        onClick={onToggle}
      >
        <span className="chat-category-label">{label}</span>
        <FontAwesomeIcon
          icon={faChevronDown}
          className={`chat-category-chevron${expanded ? '' : ' chat-category-chevron--collapsed'}`}
          aria-hidden="true"
        />
      </button>
      {expanded ? children : null}
    </section>
  )
}
