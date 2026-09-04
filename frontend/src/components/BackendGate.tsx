import type { ReactNode } from 'react'
import { BackendConnectingLoader } from './BackendConnectingLoader'
import { useBackendConnection } from '../hooks/useBackendConnection'

/** Blocks the shell until /healthz reports ready (migrate/seed finished). */
export function BackendGate({ children }: { children: ReactNode }) {
  const { isConnected, phase, error } = useBackendConnection()

  if (error) {
    return <BackendConnectingLoader errorMessage={error} />
  }

  if (!isConnected) {
    const message =
      phase === 'starting'
        ? 'Backend is initializing the database…'
        : 'Waiting for the API to listen…'
    return <BackendConnectingLoader message={message} />
  }

  return <>{children}</>
}
