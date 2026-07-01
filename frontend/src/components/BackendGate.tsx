import type { ReactNode } from 'react'
import { BackendConnectingLoader } from './BackendConnectingLoader'
import { useBackendConnection } from '../hooks/useBackendConnection'

/** Hides the app until /healthz succeeds (API listening after master DB seed). */
export function BackendGate({ children }: { children: ReactNode }) {
  const { isConnected } = useBackendConnection()

  if (!isConnected)
    return <BackendConnectingLoader />

  return <>{children}</>
}
