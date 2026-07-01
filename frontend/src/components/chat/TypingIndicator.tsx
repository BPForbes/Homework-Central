export function TypingIndicator() {
  return (
    <div className="typing-bubble" aria-label="Someone is typing">
      <span className="typing-dot" />
      <span className="typing-dot typing-dot--delay-1" />
      <span className="typing-dot typing-dot--delay-2" />
    </div>
  )
}
