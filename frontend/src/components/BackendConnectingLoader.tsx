const LOADING_BAR_INDEXES = Array.from({ length: 8 }, (_, index) => index + 1)

export function BackendConnectingLoader({
  errorMessage,
  message = 'UI connecting to backend.',
}: {
  errorMessage?: string
  message?: string
}) {
  return (
    <div
      className="backend-connecting-screen"
      role={errorMessage ? 'alert' : 'status'}
      aria-live="polite"
      aria-busy={!errorMessage}
    >
      <div className="backend-connecting-content">
        {!errorMessage && (
          <div className="backend-connecting-bars" aria-hidden="true">
            {LOADING_BAR_INDEXES.map((index) => (
              <div key={index} className={`bar bar${index}`} />
            ))}
          </div>
        )}
        <p className="backend-connecting-message">
          {errorMessage ?? message}
        </p>
      </div>
    </div>
  )
}
