import { createContext, useCallback, useContext, useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { authApi } from '../api/auth'
import type { UserInfo } from '../types/auth'

interface AuthContextValue {
  user: UserInfo | null
  isLoading: boolean
  login: (email: string, password: string) => Promise<void>
  register: (email: string, username: string, password: string) => Promise<void>
  logout: () => Promise<void>
  hasPermission: (bit: number) => boolean
}

const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserInfo | null>(null)
  const [isLoading, setIsLoading] = useState(true)

  useEffect(() => {
    const token = sessionStorage.getItem('accessToken')
    if (!token) {
      setIsLoading(false)
      return
    }
    authApi
      .me()
      .then(({ data }) => setUser(data))
      .catch(() => sessionStorage.removeItem('accessToken'))
      .finally(() => setIsLoading(false))
  }, [])

  const login = useCallback(async (email: string, password: string) => {
    const { data } = await authApi.login({ email, password })
    sessionStorage.setItem('accessToken', data.accessToken)
    setUser(data.user)
  }, [])

  const register = useCallback(async (email: string, username: string, password: string) => {
    const { data } = await authApi.register({ email, username, password })
    sessionStorage.setItem('accessToken', data.accessToken)
    setUser(data.user)
  }, [])

  const logout = useCallback(async () => {
    await authApi.logout().catch(() => {})
    sessionStorage.removeItem('accessToken')
    setUser(null)
  }, [])

  const hasPermission = useCallback(
    (bit: number): boolean => {
      if (!user?.permissionMask) return false
      const bytes = Uint8Array.from(atob(user.permissionMask), (c) => c.charCodeAt(0))
      const byteIndex = Math.floor(bit / 8)
      const bitIndex = bit % 8
      return (bytes[byteIndex] & (1 << bitIndex)) !== 0
    },
    [user]
  )

  return (
    <AuthContext.Provider value={{ user, isLoading, login, register, logout, hasPermission }}>
      {children}
    </AuthContext.Provider>
  )
}

export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used within AuthProvider')
  return ctx
}
