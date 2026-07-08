import { useEffect, useMemo, useRef, useState } from 'react'
import type { ChatMessage, ChatTypingUser, MentionRoleOption } from '../../types/chat'
import { ChatMessageBubble } from './ChatMessageBubble'
import { TypingIndicator } from './TypingIndicator'
import type { MentionStyleLookup } from './MentionContent'

interface ChatMessageListProps {
  messages: ChatMessage[]
  typingUsers: ChatTypingUser[]
  loading: boolean
  currentUserId: string | undefined
  mentionRoles?: MentionRoleOption[]
  onReply: (message: ChatMessage) => void
}

export function ChatMessageList({
  messages,
  typingUsers,
  loading,
  currentUserId,
  mentionRoles = [],
  onReply,
}: ChatMessageListProps) {
  const bottomRef = useRef<HTMLDivElement>(null)
  const containerRef = useRef<HTMLDivElement>(null)
  const [highlightedId, setHighlightedId] = useState<string | null>(null)

  const mentionStyles = useMemo((): MentionStyleLookup => {
    const userColors: Record<string, string> = {}
    const roleColors: Record<string, string> = {}

    for (const message of messages) {
      const key = message.senderUsername.toLowerCase()
      if (!userColors[key] && message.senderMessageColor)
        userColors[key] = message.senderMessageColor
    }

    for (const role of mentionRoles)
      roleColors[role.name.toLowerCase()] = role.messageColor

    return { userColors, roleColors }
  }, [messages, mentionRoles])

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages, typingUsers])

  function handleJumpToMessage(messageId: string) {
    const target = containerRef.current?.querySelector<HTMLElement>(`[data-message-id="${messageId}"]`)
    if (!target)
      return

    target.scrollIntoView({ behavior: 'smooth', block: 'center' })
    setHighlightedId(messageId)
    window.setTimeout(() => {
      setHighlightedId((current) => (current === messageId ? null : current))
    }, 1400)
  }

  if (loading) {
    return <p className="text-center text-sm text-muted-foreground py-8">Loading messages…</p>
  }

  return (
    <div ref={containerRef} className="flex-1 overflow-y-auto px-6 py-5 space-y-3" role="log" aria-live="polite">
      {messages.length === 0 && typingUsers.length === 0 && (
        <p className="text-center text-sm text-muted-foreground py-8">No messages yet. Say hello!</p>
      )}

      {messages.map((message) => (
        <ChatMessageBubble
          key={message.messageId}
          message={message}
          isOwn={message.senderId === currentUserId}
          highlighted={highlightedId === message.messageId}
          mentionStyles={mentionStyles}
          onReply={onReply}
          onJumpToMessage={handleJumpToMessage}
        />
      ))}

      {typingUsers.length > 0 && <TypingIndicator typingUsers={typingUsers} />}

      <div ref={bottomRef} />
    </div>
  )
}
