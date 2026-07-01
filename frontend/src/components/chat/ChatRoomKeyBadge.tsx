import type { IconDefinition } from '@fortawesome/fontawesome-svg-core'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faKey } from '@fortawesome/free-solid-svg-icons'

interface ChatRoomKeyBadgeProps {
  roomIcon: IconDefinition
}

export function ChatRoomKeyBadge({ roomIcon }: ChatRoomKeyBadgeProps) {
  return (
    <div className="chat-key-badge" aria-hidden="true">
      <span className="fa-layers fa-fw">
        <FontAwesomeIcon icon={roomIcon} />
        <FontAwesomeIcon
          icon={faKey}
          transform="shrink-8 down-10 left-10"
          style={{ color: '#ffcc00' }}
        />
      </span>
    </div>
  )
}
