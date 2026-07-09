import { useRef, useState } from 'react'
import type { PointerEvent as ReactPointerEvent } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faCopy, faReply } from '@fortawesome/free-solid-svg-icons'
import type { ChatMessage } from '../../types/chat'
import { RichContent } from '../../richtext/RichContent'
import type { MentionStyleLookup } from '../../richtext/markdown'

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
    // Swipe-to-reply is only offered on other people's (white) messages — replying to your own
    // message isn't a supported action, matching the hover/reply affordance below.
    if (isOwn || event.pointerType === 'mouse')
      return
    dragState.current = { pointerId: event.pointerId, startX: event.clientX, currentX: 0, dragging: true }
  }

  function handlePointerMove(event: ReactPointerEvent<HTMLElement>) {
    const state = dragState.current
    if (!state || state.pointerId !== event.pointerId || !state.dragging)
      return

    const deltaX = event.clientX - state.startX
    // Only care about right-to-left swipes; clamp so the bubble never drags further than
    // SWIPE_MAX_PX off its resting position.
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
      // Clipboard access can be denied by the browser; silently no-op rather than surface an
      // error for what is a convenience action.
    }
  }

  const swipeProgress = Math.min(1, Math.abs(dragX) / SWIPE_TRIGGER_PX)
  const hasReply = Boolean(message.replyToMessageId)

  return (
    <div className={`chat-bubble-row ${isOwn ? 'chat-bubble-row--own' : 'chat-bubble-row--other'}`}>
      {!isOwn && (
        <span
          className="chat-bubble-swipe-hint"
          style={{ opacity: swipeProgress, transform: `scale(${0.6 + 0.4 * swipeProgress})` }}
          aria-hidden="true"
        >
          <FontAwesomeIcon icon={faReply} />
        </span>
      )}
      <div
        className={`chat-message-thread ${isOwn ? 'chat-message-thread--own' : 'chat-message-thread--other'} ${hasReply ? 'chat-message-thread--has-reply' : ''}`}
      >
        {hasReply && (
          <>
            <button
              type="button"
              className="chat-reply-quote-bubble"
              onClick={() => onJumpToMessage(message.replyToMessageId!)}
            >
              <FontAwesomeIcon icon={faReply} className="chat-reply-quote-bubble-icon" />
              <span className="chat-reply-quote-bubble-text">
                <span className="chat-reply-quote-bubble-sender">
                  {message.replyToSenderUsername ?? 'Unknown'}
                </span>
                <span className="chat-reply-quote-bubble-content">
                  {message.replyToContentSnippet ?? ''}
                </span>
              </span>
            </button>
            <span className="chat-reply-connector" aria-hidden="true" />
          </>
        )}

        <article
          data-message-id={message.messageId}
          className={`chat-bubble ${isOwn ? 'chat-bubble--own' : 'chat-bubble--other'} ${highlighted ? 'chat-bubble--highlighted' : ''}`}
          style={dragX !== 0 ? { transform: `translateX(${dragX}px)`, transition: 'none' } : undefined}
          onPointerDown={handlePointerDown}
          onPointerMove={handlePointerMove}
          onPointerUp={(event) => endDrag(event)}
          onPointerCancel={(event) => endDrag(event, true)}
        >
          {!isOwn && message.senderUsername && (
            <div
              className="chat-bubble-sender"
              style={message.senderMessageColor ? { color: message.senderMessageColor } : undefined}
            >
              {message.senderUsername}
            </div>
          )}

          <div className="chat-bubble-content">
            <RichContent content={message.content} mentionStyles={mentionStyles} />
          </div>
          <time className="chat-bubble-time" dateTime={message.createdAtUtc}>
            {formatUtcTimestamp(message.createdAtUtc)}
          </time>

          <div className="chat-bubble-actions" role="toolbar" aria-label="Message actions">
            {!isOwn && (
              <button
                type="button"
                className="chat-bubble-action-btn"
                onClick={() => onReply(message)}
                title="Reply"
                aria-label="Reply to this message"
              >
                <FontAwesomeIcon icon={faReply} />
              </button>
            )}
            <button
              type="button"
              className="chat-bubble-action-btn"
              onClick={() => void handleCopy()}
              title={copied ? 'Copied!' : 'Copy message'}
              aria-label="Copy message content"
            >
              <FontAwesomeIcon icon={faCopy} />
            </button>
          </div>
        </article>
      </div>
    </div>
  )
}
