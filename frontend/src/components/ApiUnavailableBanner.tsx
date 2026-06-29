/** Shown on auth pages when the local API is not reachable (build failure or still starting). */
export function ApiUnavailableBanner() {
  return (
    <div className="api-unavailable" role="alert">
      <span className="api-unavailable-icon" aria-hidden="true">
        <span className="api-unavailable-x">X</span>
      </span>
      <span>unable to connect to API</span>
    </div>
  )
}
