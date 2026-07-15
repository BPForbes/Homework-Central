const LOADING_BAR_INDEXES = Array.from({ length: 8 }, (_, index) => index + 1)

interface LoadingBarsProps {
  errorMessage?: string
  message?: string
}

/** Shared transparent loading indicator for route and data waits. */
export function LoadingBars({
  errorMessage,
  message = 'Loading…',
}: LoadingBarsProps) {
  return (
    <div
      className="backend-connecting-content"
      role={errorMessage ? 'alert' : 'status'}
      aria-live="polite"
      aria-busy={!errorMessage}
    >
      {!errorMessage && (
        <div className="backend-connecting-bars" aria-hidden="true">
          {LOADING_BAR_INDEXES.map((index) => (
            <div key={index} className={'bar bar' + index} />
          ))}
        </div>
      )}
      <p className="backend-connecting-message">{errorMessage ?? message}</p>
    </div>
  )
}
