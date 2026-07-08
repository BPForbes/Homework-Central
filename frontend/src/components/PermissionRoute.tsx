import { Navigate } from 'react-router-dom'
import type { ReactNode } from 'react'
import { useAuth } from '../context/AuthContext'

interface Props {
  permissionBit: number
  children: ReactNode
}

/** Redirects to dashboard when the signed-in user lacks a moderation permission bit. */
export function PermissionRoute({ permissionBit, children }: Props) {
  const { hasPermission, isLoading } = useAuth()

  if (isLoading) {
    return (
      <div className="loading-screen">
        <div className="spinner" />
      </div>
    )
  }

  if (!hasPermission(permissionBit)) {
    return <Navigate to="/dashboard" replace />
  }

  return <>{children}</>
}
