import type { ChatTypingUser } from '../../types/chat'

interface TypingIndicatorProps {
  typingUsers: ChatTypingUser[]
}

function formatTypingLabel(typingUsers: ChatTypingUser[]): string {
  const names = typingUsers.map((user) => user.username)
  if (names.length === 1)
    return names[0]
  if (names.length === 2)
    return `${names[0]} and ${names[1]}`
  return `${names.slice(0, -1).join(', ')}, and ${names[names.length - 1]}`
}

export function TypingIndicator({ typingUsers }: TypingIndicatorProps) {
  if (typingUsers.length === 0)
    return null

  const label = formatTypingLabel(typingUsers)

  return (
    <div className="typing-indicator">
      <div className="typing-indicator-sender">{label}</div>
      <div className="typing-bubble" aria-label={`${label} is typing`}>
        <span className="typing-dot" />
        <span className="typing-dot typing-dot--delay-1" />
        <span className="typing-dot typing-dot--delay-2" />
      </div>
    </div>
  )
}
