import { useRef, useState } from 'react'
import type { PointerEvent as ReactPointerEvent } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faCopy, faReply } from '@fortawesome/free-solid-svg-icons'
import { byPrefixAndName } from '../../icons/byPrefixAndName'
import type { ChatMessage } from '../../types/chat'
import { RichContent } from '../../richtext/RichContent'
import type { MentionStyleLookup } from '../../richtext/markdown'
import { formatUtcTimestamp } from '../../utils/formatUtcTimestamp'

interface ChatMessageBubbleProps {
  message: ChatMessage
  isOwn: boolean
  highlighted?: boolean
  mentionStyles?: MentionStyleLookup
  /** When false, score and upvote/downvote controls are not rendered (ticket rooms). */
  votesEnabled?: boolean
  onReply: (message: ChatMessage) => void
  onJumpToMessage: (messageId: string) => void
  onVote?: (message: ChatMessage, value: 1 | -1) => void
  onReport?: (message: ChatMessage) => void
}

const SWIPE_TRIGGER_PX = 56
const SWIPE_MAX_PX = 84

export function ChatMessageBubble({
  message,
  isOwn,
  highlighted = false,
  mentionStyles,
  votesEnabled = true,
  onReply,
  onJumpToMessage,
  onVote,
  onReport,
}: ChatMessageBubbleProps) {
  const [dragX, setDragX] = useState(0)
  const [copied, setCopied] = useState(false)
  const dragState = useRef<{ pointerId: number; startX: number; currentX: number; dragging: boolean } | null>(null)
  const score = message.score ?? 0
  const canVote = Boolean(votesEnabled && !isOwn && onVote)

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
    const next = Math.max(-SWIPE_MAX_PX, Math.min(0, deltaX))
    state.currentX = next
    setDragX(next)
  }

  function endDrag(event: ReactPointerEvent<HTMLElement>, cancelled = false) {
    const state = dragState.current
    if (!state || state.pointerId !== event.pointerId)
      return
    dragState.current = null
    const shouldReply = !cancelled && state.currentX <= -SWIPE_TRIGGER_PX
    setDragX(0)
    if (shouldReply)
      onReply(message)
  }

  async function handleCopy() {
    try {
      await navigator.clipboard.writeText(message.content)
      setCopied(true)
      window.setTimeout(() => setCopied(false), 1200)
    } catch {
      setCopied(false)
    }
  }

  return (
    <article
      data-message-id={message.messageId}
      className={`chat-thread-item ${highlighted ? 'chat-thread-item--highlighted' : ''}`}
      style={dragX !== 0 ? { transform: `translateX(${dragX}px)`, transition: 'none' } : undefined}
      onPointerDown={handlePointerDown}
      onPointerMove={handlePointerMove}
      onPointerUp={(event) => endDrag(event)}
      onPointerCancel={(event) => endDrag(event, true)}
    >
      <header className="chat-thread-item-header">
        <div
          className="chat-thread-sender"
          style={message.senderMessageColor ? { color: message.senderMessageColor } : undefined}
        >
          {message.senderUsername || 'Unknown'}:
        </div>
        {votesEnabled && (
          <div className="chat-thread-votes" role="group" aria-label="Message votes">
            <button
              type="button"
              className={`chat-thread-action-btn ${message.viewerVote === 'up' ? 'is-active' : ''}`}
              onClick={() => onVote?.(message, 1)}
              disabled={!canVote}
              title="Upvote"
              aria-label="Upvote message"
            >
              <FontAwesomeIcon icon={byPrefixAndName.fas['arrow-down']} flip="vertical" />
            </button>
            <span className="chat-thread-score" aria-label={`Score ${score}`}>
              {score}
            </span>
            <button
              type="button"
              className={`chat-thread-action-btn ${message.viewerVote === 'down' ? 'is-active' : ''}`}
              onClick={() => onVote?.(message, -1)}
              disabled={!canVote}
              title="Downvote"
              aria-label="Downvote message"
            >
              <FontAwesomeIcon icon={byPrefixAndName.fas['arrow-down']} />
            </button>
          </div>
        )}
      </header>

      {message.replyToMessageId && (
        <button
          type="button"
          className="chat-reply-snippet"
          onClick={() => onJumpToMessage(message.replyToMessageId!)}
        >
          <span className="chat-reply-snippet-author">{message.replyToSenderUsername}</span>
          <span className="chat-reply-snippet-text">{message.replyToContentSnippet}</span>
        </button>
      )}

      {message.forwardedFrom && (
        <div className="chat-forward-card">
          <div className="chat-forward-card-label">Forwarded from {message.forwardedFrom.sourceSenderUsername}</div>
          <div className="chat-forward-card-body">{message.forwardedFrom.contentSnippet}</div>
        </div>
      )}

      <div className="chat-thread-content">
        <RichContent content={message.content} mentionStyles={mentionStyles} />
      </div>

      {message.attachments && message.attachments.length > 0 && (
        <ul className="chat-attachment-list">
          {message.attachments.map((attachment) => (
            <li key={attachment.attachmentId}>
              {attachment.contentType.startsWith('image/') ? (
                <img
                  src={attachment.downloadUrl}
                  alt={attachment.fileName}
                  className="chat-attachment-image"
                />
              ) : (
                <a href={attachment.downloadUrl} target="_blank" rel="noreferrer">
                  {attachment.fileName}
                </a>
              )}
            </li>
          ))}
        </ul>
      )}

      {message.linkPreviews && message.linkPreviews.length > 0 && (
        <ul className="chat-link-preview-list">
          {message.linkPreviews.map((preview) => (
            <li key={preview.url} className="chat-link-preview-card">
              <a href={preview.url} target="_blank" rel="noreferrer">
                {preview.title || preview.url}
              </a>
              {preview.description && <p>{preview.description}</p>}
            </li>
          ))}
        </ul>
      )}

      <footer className="chat-thread-item-footer">
        <div className="chat-thread-actions" role="toolbar" aria-label="Message actions">
          {!isOwn && onReport && (
            <button
              type="button"
              className="chat-thread-action-btn"
              onClick={() => onReport(message)}
              title="Report"
              aria-label="Report message"
            >
              <FontAwesomeIcon icon={byPrefixAndName.fas.exclamation} />
            </button>
          )}
          <button
            type="button"
            className="chat-thread-action-btn"
            onClick={() => void handleCopy()}
            title={copied ? 'Copied!' : 'Copy message'}
            aria-label="Copy message content"
          >
            <FontAwesomeIcon icon={faCopy} />
          </button>
          {!isOwn && (
            <button
              type="button"
              className="chat-thread-action-btn"
              onClick={() => onReply(message)}
              title="Reply"
              aria-label="Reply to this message"
            >
              <FontAwesomeIcon icon={faReply} />
            </button>
          )}
        </div>
        <time className="chat-thread-time" dateTime={message.createdAtUtc}>
          {formatUtcTimestamp(message.createdAtUtc)}
        </time>
      </footer>
    </article>
  )
}
