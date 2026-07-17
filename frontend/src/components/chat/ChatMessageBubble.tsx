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
  const hasReply = Boolean(message.replyToMessageId)

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
    <div className={`chat-bubble-row ${isOwn ? 'chat-bubble-row--own' : 'chat-bubble-row--other'}`}>
      <div
        className={[
          'chat-message-thread',
          isOwn ? 'chat-message-thread--own' : 'chat-message-thread--other',
          hasReply ? 'chat-message-thread--has-reply' : '',
        ].filter(Boolean).join(' ')}
      >
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

          <div className="chat-bubble-content">
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

          <time className="chat-bubble-time" dateTime={message.createdAtUtc}>
            {formatUtcTimestamp(message.createdAtUtc)}
          </time>

          <div className="chat-bubble-actions" role="toolbar" aria-label="Message actions">
            {votesEnabled && (
              <span className="chat-bubble-score-chip" aria-label={`Score ${score}`}>
                {score}
              </span>
            )}
            {votesEnabled && !isOwn && onVote && (
              <>
                <button
                  type="button"
                  className={`chat-bubble-action-btn ${message.viewerVote === 'up' ? 'is-active' : ''}`}
                  onClick={() => onVote(message, 1)}
                  title="Upvote"
                  aria-label="Upvote message"
                >
                  <FontAwesomeIcon icon={byPrefixAndName.fas['arrow-down']} flip="vertical" />
                </button>
                <button
                  type="button"
                  className={`chat-bubble-action-btn ${message.viewerVote === 'down' ? 'is-active' : ''}`}
                  onClick={() => onVote(message, -1)}
                  title="Downvote"
                  aria-label="Downvote message"
                >
                  <FontAwesomeIcon icon={byPrefixAndName.fas['arrow-down']} />
                </button>
              </>
            )}
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
            {!isOwn && onReport && (
              <button
                type="button"
                className="chat-bubble-action-btn"
                onClick={() => onReport(message)}
                title="Report"
                aria-label="Report message"
              >
                <FontAwesomeIcon icon={byPrefixAndName.fas.exclamation} />
              </button>
            )}
          </div>
        </article>
      </div>
    </div>
  )
}
