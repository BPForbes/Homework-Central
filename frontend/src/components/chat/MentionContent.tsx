import type { ReactNode } from 'react'

const MENTION_PATTERN = /@([\p{L}\p{N}_][\p{L}\p{N}_.-]*)/gu
const BROADCAST_TOKENS = new Set(['everyone', 'here'])
const NULL_TOKEN = 'null'

export interface MentionStyleLookup {
  userColors: Record<string, string>
  roleColors: Record<string, string>
}

interface MentionContentProps {
  content: string
  mentionStyles?: MentionStyleLookup
}

function resolveMentionColor(token: string, mentionStyles?: MentionStyleLookup): string | undefined {
  if (!mentionStyles)
    return undefined

  const lower = token.toLowerCase()
  if (mentionStyles.userColors[lower])
    return mentionStyles.userColors[lower]

  if (mentionStyles.roleColors[lower])
    return mentionStyles.roleColors[lower]

  return undefined
}

export function MentionContent({ content, mentionStyles }: MentionContentProps) {
  const parts: ReactNode[] = []
  let lastIndex = 0
  let match: RegExpExecArray | null

  MENTION_PATTERN.lastIndex = 0
  while ((match = MENTION_PATTERN.exec(content)) !== null) {
    if (match.index > lastIndex)
      parts.push(content.slice(lastIndex, match.index))

    const token = match[1]
    const isNull = token.toLowerCase() === NULL_TOKEN
    const isBroadcast = BROADCAST_TOKENS.has(token.toLowerCase())
    const color = resolveMentionColor(token, mentionStyles)

    if (isNull) {
      parts.push(<span key={`${match.index}-null`}>@{token}</span>)
    } else {
      parts.push(
        <span
          key={`${match.index}-mention`}
          className={`chat-mention${isBroadcast ? ' chat-mention--broadcast' : ''}`}
          style={color ? { color, backgroundColor: `${color}22` } : undefined}
        >
          @{token}
        </span>
      )
    }

    lastIndex = match.index + match[0].length
  }

  if (lastIndex < content.length)
    parts.push(content.slice(lastIndex))

  return <>{parts}</>
}
