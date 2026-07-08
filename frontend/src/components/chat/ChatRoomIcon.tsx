import type { IconDefinition } from '@fortawesome/fontawesome-svg-core'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faKey } from '@fortawesome/free-solid-svg-icons'
import { cn } from '../../lib/utils'

interface ChatRoomIconProps {
  icon: IconDefinition
  isPrivate?: boolean
  className?: string
  /** @deprecated Layering is handled internally; kept for call-site compatibility. */
  layeredClassName?: string
}

/** Room icon with optional private key overlay (blueprint-driven). */
export function ChatRoomIcon({
  icon,
  isPrivate = false,
  className,
  layeredClassName: _layeredClassName,
}: ChatRoomIconProps) {
  if (!isPrivate) {
    return <FontAwesomeIcon icon={icon} className={cn('shrink-0', className)} />
  }

  return (
    <span className={cn('relative inline-flex shrink-0 items-center justify-center', className)}>
      <FontAwesomeIcon icon={icon} />
      <FontAwesomeIcon
        icon={faKey}
        className="absolute -bottom-0.5 -right-1 text-[0.55em]"
        style={{ color: '#ffcc00' }}
      />
    </span>
  )
}
