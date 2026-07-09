import { useRef, useState } from 'react'
import type { FormEvent, KeyboardEvent } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faArrowUp } from '@fortawesome/free-solid-svg-icons'
import type { MockAccount } from './mockAccounts'
import { RichTextToolbar } from '../../richtext/RichTextToolbar'
import { RichContent } from '../../richtext/RichContent'
import { FormattingToggleButton } from '../../richtext/FormattingToggleButton'

interface MockMessage {
  id: string
  accountId: string
  content: string
  createdAt: number
}

interface ChatPreviewPanelProps {
  channelDisplayName: string
  mockAccounts: MockAccount[]
  activeMockId: string | null
}

let mockMessageSeq = 0

/**
 * Fully client-side chat simulation for the channel builder preview. Nothing here reaches the
 * backend or SignalR — messages live only in this component's state and vanish on navigation.
 */
export function ChatPreviewPanel({ channelDisplayName, mockAccounts, activeMockId }: ChatPreviewPanelProps) {
  const [messages, setMessages] = useState<MockMessage[]>([])
  const [draft, setDraft] = useState('')
  const [showFormatting, setShowFormatting] = useState(false)
  const bottomRef = useRef<HTMLDivElement>(null)
  const textareaRef = useRef<HTMLTextAreaElement>(null)

  const activeAccount = mockAccounts.find((a) => a.id === activeMockId) ?? null

  function sendMessage() {
    if (!draft.trim() || !activeMockId) return
    mockMessageSeq += 1
    setMessages((prev) => [
      ...prev,
      { id: `mock-msg-${mockMessageSeq}`, accountId: activeMockId, content: draft, createdAt: Date.now() },
    ])
    setDraft('')
    setShowFormatting(false)
    window.requestAnimationFrame(() => bottomRef.current?.scrollIntoView({ behavior: 'smooth' }))
  }

  function handleSubmit(e: FormEvent) {
    e.preventDefault()
    sendMessage()
  }

  function handleKeyDown(e: KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === 'Enter' && !e.shiftKey && !e.nativeEvent.isComposing) {
      e.preventDefault()
      sendMessage()
    }
  }

  return (
    <div className="chat-preview-panel">
      <p className="server-page-subtitle">
        Simulated conversation in <strong>{channelDisplayName}</strong> between your mock accounts. Nothing here is
        saved, delivered to the inbox, or visible to real members.
      </p>

      <div className="chat-preview-messages">
        {messages.length === 0 && <p className="chat-messages-empty">No messages yet. Send one as the active mock account.</p>}
        {messages.map((message) => {
          const sender = mockAccounts.find((a) => a.id === message.accountId)
          const isActive = message.accountId === activeMockId
          return (
            <div
              key={message.id}
              className={`chat-bubble-row ${isActive ? 'chat-bubble-row--own' : 'chat-bubble-row--other'}`}
            >
              <div className={`chat-message-thread ${isActive ? 'chat-message-thread--own' : 'chat-message-thread--other'}`}>
                <article className={`chat-bubble ${isActive ? 'chat-bubble--own' : 'chat-bubble--other'}`}>
                  <div className="chat-bubble-sender" style={sender ? { color: sender.color } : undefined}>
                    {sender?.label ?? 'Unknown mock account'}
                  </div>
                  <div className="chat-bubble-content">
                    <RichContent content={message.content} />
                  </div>
                </article>
              </div>
            </div>
          )
        })}
        <div ref={bottomRef} />
      </div>

      <div className="chat-composer-toolbar-row">
        {!showFormatting && (
          <RichTextToolbar textareaRef={textareaRef} value={draft} onChange={setDraft} compact />
        )}
        <FormattingToggleButton
          active={showFormatting}
          onToggle={() => setShowFormatting((prev) => !prev)}
          disabled={!activeAccount}
        />
      </div>
      <form className="chat-composer" onSubmit={handleSubmit}>
        <div className="chat-composer-input-wrap">
          {showFormatting ? (
            <div className="rich-preview-pane chat-composer-preview-pane">
              {draft.trim() ? <RichContent content={draft} /> : <p className="chat-messages-empty">Nothing to preview yet.</p>}
            </div>
          ) : (
            <textarea
              ref={textareaRef}
              className="chat-composer-input"
              value={draft}
              onChange={(e) => setDraft(e.target.value)}
              onKeyDown={handleKeyDown}
              placeholder={activeAccount ? `Message as ${activeAccount.label}…` : 'Add a mock account above to chat'}
              rows={1}
              disabled={!activeAccount}
            />
          )}
        </div>
        <button type="submit" className="chat-send-btn" disabled={!activeAccount || !draft.trim()} aria-label="Send message">
          <FontAwesomeIcon icon={faArrowUp} />
        </button>
      </form>
    </div>
  )
}
