import type { IconDefinition } from '@fortawesome/fontawesome-svg-core'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faKey } from '@fortawesome/free-solid-svg-icons'

interface ChatRoomIconProps {
  icon: IconDefinition
  isPrivate?: boolean
  className?: string
  layeredClassName?: string
}

/** Room icon with optional private key overlay (blueprint-driven). */
export function ChatRoomIcon({
  icon,
  isPrivate = false,
  className,
  layeredClassName = 'chat-room-icon-layered',
}: ChatRoomIconProps) {
  if (!isPrivate) {
    return <FontAwesomeIcon icon={icon} className={className} />
  }

  return (
    <span className={`fa-layers fa-fw ${layeredClassName} ${className ?? ''}`.trim()}>
      <FontAwesomeIcon icon={icon} />
      <FontAwesomeIcon
        icon={faKey}
        transform="shrink-8 down-10 left-10"
        style={{ color: '#ffcc00' }}
      />
    </span>
  )
}
