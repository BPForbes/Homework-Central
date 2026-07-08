import { useRef, useState } from 'react'
import type { PointerEvent as ReactPointerEvent } from 'react'
import { CornerUpLeft, Copy, Reply } from 'lucide-react'
import type { ChatMessage } from '../../types/chat'
import { MentionContent, type MentionStyleLookup } from './MentionContent'
import { cn } from '../../lib/utils'

interface ChatMessageBubbleProps {
  message: ChatMessage
  isOwn: boolean
  highlighted?: boolean
  mentionStyles?: MentionStyleLookup
  onReply: (message: ChatMessage) => void
  onJumpToMessage: (messageId: string) => void
}

const SWIPE_TRIGGER_PX = 56
const SWIPE_MAX_PX = 84

function formatUtcTimestamp(iso: string): string {
  const date = new Date(iso)
  const year = date.getUTCFullYear()
  const month = String(date.getUTCMonth() + 1).padStart(2, '0')
  const day = String(date.getUTCDate()).padStart(2, '0')
  const hours = String(date.getUTCHours()).padStart(2, '0')
  const minutes = String(date.getUTCMinutes()).padStart(2, '0')
  return `${year}-${month}-${day} ${hours}:${minutes} UTC`
}

function AuthorAvatar({ initials, color }: { initials: string; color: string }) {
  return (
    <div
      className="w-9 h-9 rounded-full flex items-center justify-center text-xs font-semibold text-white shrink-0"
      style={{ backgroundColor: color }}
    >
      {initials}
    </div>
  )
}

function initialsFromUsername(username: string): string {
  const parts = username.trim().split(/\s+/)
  if (parts.length >= 2)
    return (parts[0][0] + parts[1][0]).toUpperCase()
  return username.slice(0, 2).toUpperCase()
}

export function ChatMessageBubble({
  message,
  isOwn,
  highlighted = false,
  mentionStyles,
  onReply,
  onJumpToMessage,
}: ChatMessageBubbleProps) {
  const [dragX, setDragX] = useState(0)
  const [copied, setCopied] = useState(false)
  const dragState = useRef<{ pointerId: number; startX: number; currentX: number; dragging: boolean } | null>(null)

  function handlePointerDown(event: ReactPointerEvent<HTMLElement>) {
    if (isOwn || event.pointerType === 'mouse')
      return
    dragState.current = { pointerId: event.pointerId, startX: event.clientX, currentX: 0, dragging: true }
  }

  function handlePointerMove(event: ReactPointerEvent<HTMLElement>) {
    const state = dragState.current
    if (!state || state.pointerId !== event.pointerId || !state.dragging)
      return
    const deltaX = event.clientX - state.startX
    const clamped = Math.max(-SWIPE_MAX_PX, Math.min(0, deltaX))
    state.currentX = clamped
    setDragX(clamped)
  }

  function endDrag(event: ReactPointerEvent<HTMLElement>, cancelled = false) {
    const state = dragState.current
    if (!state || state.pointerId !== event.pointerId)
      return
    const finalX = state.currentX
    dragState.current = null
    if (!cancelled && Math.abs(finalX) >= SWIPE_TRIGGER_PX)
      onReply(message)
    setDragX(0)
  }

  async function handleCopy() {
    try {
      await navigator.clipboard.writeText(message.content)
      setCopied(true)
      window.setTimeout(() => setCopied(false), 1500)
    } catch {
      // Clipboard denied — no-op
    }
  }

  const swipeProgress = Math.min(1, Math.abs(dragX) / SWIPE_TRIGGER_PX)
  const hasReply = Boolean(message.replyToMessageId)
  const senderColor = message.senderMessageColor ?? '#3b5bdb'
  const senderInitials = message.senderUsername ? initialsFromUsername(message.senderUsername) : '?'

  return (
    <div className={cn('flex w-full min-w-0', isOwn ? 'justify-end' : 'justify-start')}>
      <div className={cn('max-w-[min(75%,28rem)] min-w-0 flex flex-col gap-0.5', isOwn ? 'items-end' : 'items-start')}>
        {hasReply && (
          <button
            type="button"
            onClick={() => onJumpToMessage(message.replyToMessageId!)}
            className="flex items-start gap-1.5 px-3 py-2 rounded-xl text-xs text-muted-foreground mb-0.5 border border-border/60 bg-secondary/60 text-left"
          >
            <CornerUpLeft size={11} className="shrink-0 mt-0.5 text-primary" />
            <div className="min-w-0">
              <span className="font-semibold text-primary mr-1">{message.replyToSenderUsername ?? 'Unknown'}</span>
              <span className="truncate block">{message.replyToContentSnippet ?? ''}</span>
            </div>
          </button>
        )}

        <div className={cn('group relative flex items-end gap-2', isOwn && 'flex-row-reverse')}>
          {!isOwn && swipeProgress > 0 && (
            <span
              className="absolute left-0 text-primary"
              style={{ opacity: swipeProgress, transform: `scale(${0.6 + 0.4 * swipeProgress})` }}
              aria-hidden="true"
            >
              <Reply size={14} />
            </span>
          )}

          {!isOwn && (
            <AuthorAvatar initials={senderInitials} color={senderColor} />
          )}

          <article
            data-message-id={message.messageId}
            className={cn(
              'relative px-4 py-2.5 rounded-2xl text-sm leading-relaxed whitespace-pre-wrap break-words [overflow-wrap:anywhere] max-w-full min-w-0 transition-shadow',
              isOwn
                ? 'bg-primary text-white rounded-br-md'
                : 'bg-card text-foreground rounded-bl-md shadow-sm border border-border/40',
              highlighted && 'ring-2 ring-primary/40',
            )}
            style={dragX !== 0 ? { transform: `translateX(${dragX}px)`, transition: 'none' } : undefined}
            onPointerDown={handlePointerDown}
            onPointerMove={handlePointerMove}
            onPointerUp={(event) => endDrag(event)}
            onPointerCancel={(event) => endDrag(event, true)}
          >
            {!isOwn && message.senderUsername && (
              <div className="text-xs font-semibold mb-1" style={{ color: senderColor }}>
                {message.senderUsername}
              </div>
            )}
            <div>
              <MentionContent content={message.content} mentionStyles={mentionStyles} />
            </div>
            <time
              className={cn(
                'block text-[10px] mt-1 font-[family-name:var(--font-label-mono)]',
                isOwn ? 'text-blue-200' : 'text-muted-foreground',
              )}
              dateTime={message.createdAtUtc}
            >
              {formatUtcTimestamp(message.createdAtUtc)}
            </time>

            <div
              className="absolute top-[-0.85rem] opacity-0 group-hover:opacity-100 transition-opacity flex gap-1 p-0.5 rounded-full bg-card border border-border shadow-sm"
              style={isOwn ? { left: '-0.4rem' } : { right: '-0.4rem' }}
              role="toolbar"
              aria-label="Message actions"
            >
              {!isOwn && (
                <button
                  type="button"
                  onClick={() => onReply(message)}
                  title="Reply"
                  aria-label="Reply to this message"
                  className="w-6 h-6 rounded-full flex items-center justify-center text-muted-foreground hover:text-primary hover:bg-muted"
                >
                  <Reply size={11} />
                </button>
              )}
              <button
                type="button"
                onClick={() => void handleCopy()}
                title={copied ? 'Copied!' : 'Copy message'}
                aria-label="Copy message content"
                className="w-6 h-6 rounded-full flex items-center justify-center text-muted-foreground hover:text-primary hover:bg-muted"
              >
                <Copy size={11} />
              </button>
            </div>
          </article>
        </div>
      </div>
    </div>
  )
}
