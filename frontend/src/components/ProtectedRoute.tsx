import { Navigate } from 'react-router-dom'
import type { ReactNode } from 'react'
import { getAccessToken } from '../api/tokenManager'
import { useAuth } from '../context/useAuth'
import { LoadingBars } from './LoadingBars'

interface Props {
  children: ReactNode
}

export function ProtectedRoute({ children }: Props) {
  const { user, isLoading } = useAuth()

  if (isLoading) {
    return (
      <div className="loading-screen">
        <LoadingBars message="Loading account…" />
      </div>
    )
  }

  if (!user) {
    if (getAccessToken()) {
      return (
        <div className="loading-screen">
          <LoadingBars message="Reconnecting…" />
        </div>
      )
    }
    return <Navigate to="/login" replace />
  }

  return <>{children}</>
}
