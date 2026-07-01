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
    if (!sent) {
      // Restore the draft so the user doesn't lose what they typed, and re-notify typing so
      // the indicator matches the composer being non-empty again — onSend's own stopTyping()
      // call already cleared it optimistically before the send was known to fail.
      setDraft(content)
      onTyping()
    }

    textareaRef.current?.focus()
  }

  function handleKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    // Enter during IME composition (e.g. selecting a candidate while typing Japanese/Chinese/
    // Korean) confirms the composition, it isn't the user asking to send the message.
    if (event.key === 'Enter' && !event.shiftKey && !event.nativeEvent.isComposing) {
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

  // The typing indicator should persist for as long as there's text in the composer, even if
  // it loses focus (e.g. the user switches tabs to check something else), so blur only clears
  // it when the composer is actually empty.
  function handleBlur() {
    if (!draft.trim())
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
        onBlur={handleBlur}
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
