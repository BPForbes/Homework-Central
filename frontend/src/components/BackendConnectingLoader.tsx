export function BackendConnectingLoader({ errorMessage }: { errorMessage?: string }) {
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
            <div className="bar bar1" />
            <div className="bar bar2" />
            <div className="bar bar3" />
            <div className="bar bar4" />
            <div className="bar bar5" />
            <div className="bar bar6" />
            <div className="bar bar7" />
            <div className="bar bar8" />
          </div>
        )}
        <p className="backend-connecting-message">
          {errorMessage ?? 'UI connecting to backend.'}
        </p>
      </div>
    </div>
  )
}
