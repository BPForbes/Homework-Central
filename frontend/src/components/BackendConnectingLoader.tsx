import { LoadingBars } from './LoadingBars'

export function BackendConnectingLoader({
  errorMessage,
  message = 'UI connecting to backend.',
}: {
  errorMessage?: string
  message?: string
}) {
  return (
    <div className="backend-connecting-screen">
      <LoadingBars errorMessage={errorMessage} message={message} />
    </div>
  )
}
