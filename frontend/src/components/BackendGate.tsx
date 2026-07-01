import type { ReactNode } from 'react'
import { BackendConnectingLoader } from './BackendConnectingLoader'
import { useBackendConnection } from '../hooks/useBackendConnection'

/** Hides the app until /healthz succeeds (API listening, including long first-time provisioning). */
export function BackendGate({ children }: { children: ReactNode }) {
  const { isConnected } = useBackendConnection()

  if (!isConnected)
    return <BackendConnectingLoader />

  return <>{children}</>
}
