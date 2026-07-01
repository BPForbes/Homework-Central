import { useEffect, useRef } from 'react'
import type { ChatMessage, ChatTypingUser } from '../../types/chat'
import { TypingIndicator } from './TypingIndicator'

interface ChatMessageListProps {
  messages: ChatMessage[]
  typingUsers: ChatTypingUser[]
  loading: boolean
}

function formatUtcTimestamp(iso: string): string {
  const date = new Date(iso)
  const year = date.getUTCFullYear()
  const month = String(date.getUTCMonth() + 1).padStart(2, '0')
  const day = String(date.getUTCDate()).padStart(2, '0')
  const hours = String(date.getUTCHours()).padStart(2, '0')
  const minutes = String(date.getUTCMinutes()).padStart(2, '0')
  return `${year}-${month}-${day} ${hours}:${minutes} UTC`
}

export function ChatMessageList({ messages, typingUsers, loading }: ChatMessageListProps) {
  const bottomRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages, typingUsers])

  if (loading) {
    return <p className="chat-messages-status">Loading messages…</p>
  }

  return (
    <div className="chat-messages" role="log" aria-live="polite">
      {messages.length === 0 && typingUsers.length === 0 && (
        <p className="chat-messages-empty">No messages yet. Say hello!</p>
      )}

      {messages.map((message) => (
        <article
          key={message.messageId}
          className={`chat-bubble ${message.isOwn ? 'chat-bubble--own' : 'chat-bubble--other'}`}
        >
          {!message.isOwn && message.senderUsername && (
            <div className="chat-bubble-sender">{message.senderUsername}</div>
          )}
          <div className="chat-bubble-content">{message.content}</div>
          <time className="chat-bubble-time" dateTime={message.createdAtUtc}>
            {formatUtcTimestamp(message.createdAtUtc)}
          </time>
        </article>
      ))}

      {typingUsers.length > 0 && <TypingIndicator />}

      <div ref={bottomRef} />
    </div>
  )
}
