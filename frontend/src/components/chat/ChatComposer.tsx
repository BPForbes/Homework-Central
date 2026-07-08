import { useEffect, useRef, useState } from 'react'
import type { FormEvent, KeyboardEvent } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faArrowUp, faReply, faXmark } from '@fortawesome/free-solid-svg-icons'
import type { ChatMessage, MentionRoleOption } from '../../types/chat'
import {
  buildMentionCandidates,
  getActiveMentionQuery,
  insertMention,
  type MentionCandidate,
} from './mentionAutocomplete'

interface ChatComposerProps {
  disabled?: boolean
  sending?: boolean
  replyTarget?: ChatMessage | null
  messages?: ChatMessage[]
  mentionRoles?: MentionRoleOption[]
  onSend: (content: string) => Promise<boolean>
  onTyping: () => void
  onStopTyping: () => void
  onCancelReply?: () => void
}

export function ChatComposer({
  disabled = false,
  sending = false,
  replyTarget = null,
  messages = [],
  mentionRoles = [],
  onSend,
  onTyping,
  onStopTyping,
  onCancelReply,
}: ChatComposerProps) {
  const [draft, setDraft] = useState('')
  const [mentionQuery, setMentionQuery] = useState<{ query: string; start: number } | null>(null)
  const [mentionIndex, setMentionIndex] = useState(0)
  const textareaRef = useRef<HTMLTextAreaElement>(null)

  const mentionCandidates = mentionQuery
    ? buildMentionCandidates(messages, mentionRoles, mentionQuery.query)
    : []

  useEffect(() => {
    if (replyTarget)
      textareaRef.current?.focus()
  }, [replyTarget])

  useEffect(() => {
    setMentionIndex(0)
  }, [mentionQuery?.query, mentionCandidates.length])

  function updateMentionState(value: string, cursorPos: number) {
    setMentionQuery(getActiveMentionQuery(value, cursorPos))
  }

  function applyMention(candidate: MentionCandidate) {
    if (!mentionQuery || !textareaRef.current)
      return

    const cursorPos = textareaRef.current.selectionStart ?? draft.length
    const { nextDraft, nextCursor } = insertMention(draft, mentionQuery.start, cursorPos, candidate)
    setDraft(nextDraft)
    setMentionQuery(null)
    window.requestAnimationFrame(() => {
      textareaRef.current?.focus()
      textareaRef.current?.setSelectionRange(nextCursor, nextCursor)
    })
    if (nextDraft.trim())
      onTyping()
    else
      onStopTyping()
  }

  async function handleSubmit(event?: FormEvent) {
    event?.preventDefault()
    if (!draft.trim() || disabled || sending)
      return

    const content = draft
    setDraft('')
    setMentionQuery(null)
    const sent = await onSend(content)
    if (!sent) {
      setDraft(content)
      onTyping()
    }

    textareaRef.current?.focus()
  }

  function handleKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (mentionCandidates.length > 0 && mentionQuery) {
      if (event.key === 'ArrowDown') {
        event.preventDefault()
        setMentionIndex((prev) => (prev + 1) % mentionCandidates.length)
        return
      }

      if (event.key === 'ArrowUp') {
        event.preventDefault()
        setMentionIndex((prev) => (prev - 1 + mentionCandidates.length) % mentionCandidates.length)
        return
      }

      if (event.key === 'Enter' || event.key === 'Tab') {
        event.preventDefault()
        applyMention(mentionCandidates[mentionIndex])
        return
      }

      if (event.key === 'Escape') {
        event.preventDefault()
        setMentionQuery(null)
        return
      }
    }

    if (event.key === 'Enter' && !event.shiftKey && !event.nativeEvent.isComposing) {
      event.preventDefault()
      void handleSubmit()
    }
  }

  function handleChange(value: string) {
    setDraft(value)
    const cursorPos = textareaRef.current?.selectionStart ?? value.length
    updateMentionState(value, cursorPos)
    if (value.trim())
      onTyping()
    else
      onStopTyping()
  }

  function handleBlur() {
    if (!draft.trim())
      onStopTyping()
    window.setTimeout(() => setMentionQuery(null), 120)
  }

  function handleSelect() {
    const cursorPos = textareaRef.current?.selectionStart ?? draft.length
    updateMentionState(draft, cursorPos)
  }

  return (
    <div className="chat-composer-wrap">
      {replyTarget && (
        <div className="chat-reply-preview">
          <FontAwesomeIcon icon={faReply} className="chat-reply-preview-icon" />
          <div className="chat-reply-preview-body">
            <span className="chat-reply-preview-sender">Replying to {replyTarget.senderUsername}</span>
            <span className="chat-reply-preview-content">{replyTarget.content}</span>
          </div>
          <button
            type="button"
            className="chat-reply-preview-cancel"
            onClick={onCancelReply}
            aria-label="Cancel reply"
            title="Cancel reply"
          >
            <FontAwesomeIcon icon={faXmark} />
          </button>
        </div>
      )}
      <form className="chat-composer" onSubmit={handleSubmit}>
        <div className="chat-composer-input-wrap">
          {mentionCandidates.length > 0 && mentionQuery && (
            <ul className="chat-mention-autocomplete" role="listbox" aria-label="Mention suggestions">
              {mentionCandidates.map((candidate, index) => (
                <li key={`${candidate.kind}-${candidate.name}`} role="presentation">
                  <button
                    type="button"
                    role="option"
                    aria-selected={index === mentionIndex}
                    className={`chat-mention-autocomplete-item ${index === mentionIndex ? 'selected' : ''}`}
                    onMouseDown={(event) => {
                      event.preventDefault()
                      applyMention(candidate)
                    }}
                  >
                    <span className="chat-mention-autocomplete-at" style={{ color: candidate.color }}>@</span>
                    <span className="chat-mention-autocomplete-name" style={{ color: candidate.color }}>
                      {candidate.name}
                    </span>
                    {candidate.kind === 'role' && (
                      <span className="chat-mention-autocomplete-kind">
                        {candidate.isCustom ? 'custom role' : 'role'}
                      </span>
                    )}
                  </button>
                </li>
              ))}
            </ul>
          )}
          <textarea
            ref={textareaRef}
            className="chat-composer-input"
            value={draft}
            onChange={(event) => handleChange(event.target.value)}
            onKeyDown={handleKeyDown}
            onBlur={handleBlur}
            onSelect={handleSelect}
            onClick={handleSelect}
            placeholder={replyTarget ? `Reply to ${replyTarget.senderUsername}…` : 'Message'}
            rows={1}
            disabled={disabled || sending}
            aria-label="Message"
          />
        </div>
        <button
          type="submit"
          className="chat-send-btn"
          disabled={disabled || sending || !draft.trim()}
          aria-label="Send message"
        >
          <FontAwesomeIcon icon={faArrowUp} />
        </button>
      </form>
    </div>
  )
}
