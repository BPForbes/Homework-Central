import type { ReactNode } from 'react'

const MENTION_PATTERN = /@([A-Za-z][A-Za-z0-9_]*)/g
const BROADCAST_TOKENS = new Set(['everyone', 'here'])
const NULL_TOKEN = 'null'

interface MentionContentProps {
  content: string
}

export function MentionContent({ content }: MentionContentProps) {
  const parts: ReactNode[] = []
  let lastIndex = 0
  let match: RegExpExecArray | null

  while ((match = MENTION_PATTERN.exec(content)) !== null) {
    if (match.index > lastIndex)
      parts.push(content.slice(lastIndex, match.index))

    const token = match[1]
    const isNull = token.toLowerCase() === NULL_TOKEN
    const isBroadcast = BROADCAST_TOKENS.has(token.toLowerCase())

    if (isNull) {
      parts.push(<span key={`${match.index}-null`}>@{token}</span>)
    } else {
      parts.push(
        <span
          key={`${match.index}-mention`}
          className={`chat-mention${isBroadcast ? ' chat-mention--broadcast' : ''}`}
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
