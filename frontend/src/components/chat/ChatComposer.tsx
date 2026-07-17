import { useEffect, useMemo, useRef, useState } from 'react'
import type { ChangeEvent, FormEvent, KeyboardEvent } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faArrowUp, faPaperclip, faReply, faXmark } from '@fortawesome/free-solid-svg-icons'
import type { ChatMessage, MentionRoleOption } from '../../types/chat'
import { useAuth } from '../../context/useAuth'
import { chatApi } from '../../api/chatApi'
import {
  buildMentionCandidates,
  buildMentionStyleLookup,
  getActiveMentionQuery,
  insertMention,
  type MentionCandidate,
} from './mentionAutocomplete'
import { RichTextToolbar } from '../../richtext/RichTextToolbar'
import { RichContent } from '../../richtext/RichContent'
import { FormattingToggleButton } from '../../richtext/FormattingToggleButton'

/** Matches backend PlatformFeatures.FileUploads / ImageUploads. */
const FEATURE_FILE_UPLOADS = 15
const FEATURE_IMAGE_UPLOADS = 16

type PendingAttachment = {
  localId: string
  attachmentId: string
  fileName: string
}

interface ChatComposerProps {
  disabled?: boolean
  sending?: boolean
  replyTarget?: ChatMessage | null
  messages?: ChatMessage[]
  mentionRoles?: MentionRoleOption[]
  onSend: (content: string, attachmentIds?: string[]) => Promise<boolean>
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
  const { hasFeature } = useAuth()
  const canUpload = hasFeature(FEATURE_FILE_UPLOADS) || hasFeature(FEATURE_IMAGE_UPLOADS)
  const [draft, setDraft] = useState('')
  const [mentionQuery, setMentionQuery] = useState<{ query: string; start: number } | null>(null)
  const [mentionIndex, setMentionIndex] = useState(0)
  const [showFormatting, setShowFormatting] = useState(false)
  const [remoteUsers, setRemoteUsers] = useState<MentionCandidate[]>([])
  const [pendingAttachments, setPendingAttachments] = useState<PendingAttachment[]>([])
  const [uploading, setUploading] = useState(false)
  const [attachError, setAttachError] = useState<string | null>(null)
  const textareaRef = useRef<HTMLTextAreaElement>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)

  const localCandidates = mentionQuery
    ? buildMentionCandidates(messages, mentionRoles, mentionQuery.query)
    : []
  const mentionCandidates = mentionQuery
    ? [
        ...remoteUsers.filter((candidate) =>
          !localCandidates.some((local) => local.name.toLowerCase() === candidate.name.toLowerCase()),
        ),
        ...localCandidates,
      ]
    : []

  const mentionStyles = useMemo(
    () => buildMentionStyleLookup(messages, mentionRoles),
    [messages, mentionRoles],
  )

  const canSubmit = (draft.trim().length > 0 || pendingAttachments.length > 0) && !disabled && !sending && !uploading

  useEffect(() => {
    if (replyTarget)
      textareaRef.current?.focus()
  }, [replyTarget])

  useEffect(() => {
    setMentionIndex(0)
  }, [mentionQuery?.query, mentionCandidates.length])

  useEffect(() => {
    if (!mentionQuery?.query)
    {
      setRemoteUsers([])
      return
    }
    let cancelled = false
    const handle = window.setTimeout(() => {
      void (async () => {
        try {
          const { data } = await chatApi.searchUsers(mentionQuery.query)
          if (cancelled) return
          setRemoteUsers(data.map((user) => ({
            kind: 'user' as const,
            name: user.username,
            color: 'var(--color-ink-secondary)',
          })))
        } catch {
          if (!cancelled) setRemoteUsers([])
        }
      })()
    }, 120)
    return () => {
      cancelled = true
      window.clearTimeout(handle)
    }
  }, [mentionQuery?.query])

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

  async function uploadFiles(files: File[]) {
    setAttachError(null)
    setUploading(true)
    try {
      for (const file of files) {
        try {
          const { data } = await chatApi.uploadAttachment(file)
          setPendingAttachments((prev) => [
            ...prev,
            {
              localId: `${data.attachmentId}-${file.name}`,
              attachmentId: data.attachmentId,
              fileName: data.fileName,
            },
          ])
        } catch {
          setAttachError('Could not upload this file. Please try again.')
          break
        }
      }
    } finally {
      setUploading(false)
    }
  }

  async function handleFilesSelected(event: ChangeEvent<HTMLInputElement>) {
    const files = Array.from(event.target.files ?? [])
    event.target.value = ''
    if (files.length === 0 || !canUpload)
      return

    setAttachError(null)
    await uploadFiles(files)
  }

  function removePendingAttachment(localId: string) {
    setPendingAttachments((prev) => prev.filter((item) => item.localId !== localId))
  }

  async function handleSubmit(event?: FormEvent) {
    event?.preventDefault()
    if (!canSubmit)
      return

    const content = draft
    const attachmentsSnapshot = pendingAttachments
    const attachmentIds = attachmentsSnapshot.map((item) => item.attachmentId)
    setDraft('')
    setPendingAttachments([])
    setMentionQuery(null)
    setShowFormatting(false)
    setAttachError(null)
    const sent = await onSend(content, attachmentIds.length > 0 ? attachmentIds : undefined)
    if (!sent) {
      setDraft(content)
      setPendingAttachments(attachmentsSnapshot)
      // Attachment-only restores have blank content; do not re-broadcast typing
      // (there is no server-side typing timeout, and refocus won't stop it).
      if (content.trim())
        onTyping()
      else
        onStopTyping()
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
      {pendingAttachments.length > 0 && (
        <ul className="chat-composer-attachments" aria-label="Attachments to send">
          {pendingAttachments.map((item) => (
            <li key={item.localId} className="chat-composer-attachment-chip">
              <span className="chat-composer-attachment-name">{item.fileName}</span>
              <button
                type="button"
                className="chat-composer-attachment-remove"
                onClick={() => removePendingAttachment(item.localId)}
                aria-label={`Remove ${item.fileName}`}
                title="Remove attachment"
                disabled={sending || uploading}
              >
                <FontAwesomeIcon icon={faXmark} />
              </button>
            </li>
          ))}
        </ul>
      )}
      {attachError && <p className="chat-composer-attach-error">{attachError}</p>}
      <div className="chat-composer-toolbar-row">
        <RichTextToolbar textareaRef={textareaRef} value={draft} onChange={handleChange} compact />
        <FormattingToggleButton
          active={showFormatting}
          onToggle={() => setShowFormatting((prev) => !prev)}
          disabled={disabled}
        />
      </div>
      <form className="chat-composer" onSubmit={handleSubmit}>
        {canUpload && (
          <>
            <input
              ref={fileInputRef}
              type="file"
              className="chat-composer-file-input"
              multiple
              onChange={(event) => void handleFilesSelected(event)}
              disabled={disabled || sending || uploading}
              tabIndex={-1}
              aria-hidden
            />
            <button
              type="button"
              className="chat-attach-btn"
              onClick={() => fileInputRef.current?.click()}
              disabled={disabled || sending || uploading}
              aria-label="Attach file"
              title="Attach file"
            >
              <FontAwesomeIcon icon={faPaperclip} />
            </button>
          </>
        )}
        <div className="chat-composer-input-wrap">
          {!showFormatting && mentionCandidates.length > 0 && mentionQuery && (
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
          {showFormatting && (
            <div className="rich-preview-pane chat-composer-preview-pane">
              {draft.trim() ? (
                <RichContent content={draft} mentionStyles={mentionStyles} />
              ) : (
                <p className="chat-messages-empty">Nothing to preview yet.</p>
              )}
            </div>
          )}
          <textarea
            ref={textareaRef}
            className={`chat-composer-input ${showFormatting ? 'rich-editor-source--preview-hidden' : ''}`}
            value={draft}
            onChange={(event) => handleChange(event.target.value)}
            onKeyDown={handleKeyDown}
            onBlur={handleBlur}
            onSelect={handleSelect}
            onClick={handleSelect}
            placeholder={replyTarget ? `Reply to ${replyTarget.senderUsername}…` : 'Message'}
            rows={1}
            disabled={disabled || sending}
            readOnly={showFormatting}
            tabIndex={showFormatting ? -1 : 0}
            aria-hidden={showFormatting || undefined}
            aria-label="Message"
          />
        </div>
        <button
          type="submit"
          className="chat-send-btn"
          disabled={!canSubmit}
          aria-label="Send message"
        >
          <FontAwesomeIcon icon={faArrowUp} />
        </button>
      </form>
    </div>
  )
}
