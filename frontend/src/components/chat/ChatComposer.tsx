import { useRef, useState } from 'react'
import type { FormEvent, KeyboardEvent } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faArrowUp } from '@fortawesome/free-solid-svg-icons'

interface ChatComposerProps {
  disabled?: boolean
  sending?: boolean
  onSend: (content: string) => Promise<boolean>
  onTyping: () => void
  onStopTyping: () => void
}

export function ChatComposer({
  disabled = false,
  sending = false,
  onSend,
  onTyping,
  onStopTyping,
}: ChatComposerProps) {
  const [draft, setDraft] = useState('')
  const textareaRef = useRef<HTMLTextAreaElement>(null)

  async function handleSubmit(event?: FormEvent) {
    event?.preventDefault()
    if (!draft.trim() || disabled || sending)
      return

    const content = draft
    setDraft('')
    const sent = await onSend(content)
    if (!sent)
      setDraft(content)

    textareaRef.current?.focus()
  }

  function handleKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault()
      void handleSubmit()
    }
  }

  function handleChange(value: string) {
    setDraft(value)
    if (value.trim())
      onTyping()
    else
      onStopTyping()
  }

  return (
    <form className="chat-composer" onSubmit={handleSubmit}>
      <textarea
        ref={textareaRef}
        className="chat-composer-input"
        value={draft}
        onChange={(event) => handleChange(event.target.value)}
        onKeyDown={handleKeyDown}
        onBlur={onStopTyping}
        placeholder="Message"
        rows={1}
        disabled={disabled || sending}
        aria-label="Message"
      />
      <button
        type="submit"
        className="chat-send-btn"
        disabled={disabled || sending || !draft.trim()}
        aria-label="Send message"
      >
        <FontAwesomeIcon icon={faArrowUp} />
      </button>
    </form>
  )
}
