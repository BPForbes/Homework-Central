import { Navigate } from 'react-router-dom'
import type { ReactNode } from 'react'
import { useAuth } from '../context/AuthContext'
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
    return <Navigate to="/login" replace />
  }

  return <>{children}</>
}
