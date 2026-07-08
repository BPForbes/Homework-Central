import { useEffect, useRef, useState } from 'react'
import type { FormEvent, KeyboardEvent } from 'react'
import { AtSign, CornerUpLeft, Send, X } from 'lucide-react'
import type { ChatMessage, MentionRoleOption } from '../../types/chat'
import {
  buildMentionCandidates,
  getActiveMentionQuery,
  insertMention,
  type MentionCandidate,
} from './mentionAutocomplete'
import { cn } from '../../lib/utils'

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

  return (
    <div className="px-6 py-4 border-t border-border bg-card shrink-0 relative">
      {mentionCandidates.length > 0 && mentionQuery && (
        <div className="absolute bottom-full left-6 mb-2 bg-card border border-border rounded-xl shadow-lg overflow-hidden z-20 min-w-[200px]">
          {mentionCandidates.map((candidate, index) => (
            <button
              key={`${candidate.kind}-${candidate.name}`}
              type="button"
              onMouseDown={(e) => {
                e.preventDefault()
                applyMention(candidate)
              }}
              className={cn(
                'w-full flex items-center gap-2.5 px-3 py-2 hover:bg-muted/60 transition-colors text-left text-sm',
                index === mentionIndex && 'bg-muted/60',
              )}
            >
              <span className="font-medium text-foreground">@{candidate.name}</span>
              {candidate.kind === 'role' && (
                <span className="ml-auto text-xs text-muted-foreground">
                  {candidate.isCustom ? 'custom role' : 'role'}
                </span>
              )}
            </button>
          ))}
        </div>
      )}

      {replyTarget && (
        <div className="flex items-center justify-between gap-2 px-3 py-2 mb-2 bg-secondary/50 rounded-xl border border-border/60 text-xs text-muted-foreground">
          <div className="flex items-center gap-1.5 min-w-0">
            <CornerUpLeft size={11} className="text-primary shrink-0" />
            <span className="font-semibold text-primary">{replyTarget.senderUsername}</span>
            <span className="truncate">{replyTarget.content}</span>
          </div>
          <button
            type="button"
            onClick={onCancelReply}
            className="shrink-0 hover:text-foreground transition-colors"
            aria-label="Cancel reply"
          >
            <X size={12} />
          </button>
        </div>
      )}

      <form
        onSubmit={handleSubmit}
        className="flex items-end gap-3 bg-card border border-border rounded-2xl px-4 py-3 focus-within:border-primary/40 transition-colors"
      >
        <button
          type="button"
          className="mb-0.5 text-muted-foreground hover:text-primary transition-colors"
          onClick={() => {
            const pos = draft.length
            setDraft(`${draft}@`)
            setMentionQuery({ query: '', start: pos })
            setTimeout(() => textareaRef.current?.focus(), 0)
          }}
          aria-label="Mention someone"
        >
          <AtSign size={16} />
        </button>
        <textarea
          ref={textareaRef}
          value={draft}
          onChange={(event) => handleChange(event.target.value)}
          onKeyDown={handleKeyDown}
          onBlur={() => {
            if (!draft.trim())
              onStopTyping()
            window.setTimeout(() => setMentionQuery(null), 120)
          }}
          onSelect={() => {
            const cursorPos = textareaRef.current?.selectionStart ?? draft.length
            updateMentionState(draft, cursorPos)
          }}
          onClick={() => {
            const cursorPos = textareaRef.current?.selectionStart ?? draft.length
            updateMentionState(draft, cursorPos)
          }}
          placeholder={replyTarget ? `Reply to ${replyTarget.senderUsername}…` : 'Message'}
          rows={1}
          disabled={disabled || sending}
          aria-label="Message"
          className="flex-1 bg-transparent text-sm text-foreground placeholder:text-muted-foreground resize-none focus:outline-none leading-relaxed max-h-[120px]"
        />
        <button
          type="submit"
          disabled={disabled || sending || !draft.trim()}
          className="w-8 h-8 rounded-full bg-primary text-white flex items-center justify-center disabled:opacity-30 hover:opacity-80 transition-opacity shrink-0 mb-0.5"
          aria-label="Send message"
        >
          <Send size={13} />
        </button>
      </form>
    </div>
  )
}
