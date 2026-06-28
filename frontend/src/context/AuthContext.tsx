import { createContext, useCallback, useContext, useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { authApi } from '../api/auth'
import type { CoreMaskField, UserInfo } from '../types/auth'
import { hasMaskBit } from '../utils/bitmask'

interface AuthContextValue {
  user: UserInfo | null
  isLoading: boolean
  login: (email: string, password: string) => Promise<void>
  register: (email: string, username: string, password: string) => Promise<void>
  logout: () => Promise<void>
  hasPermission: (bit: number) => boolean
  hasFeature: (bit: number) => boolean
  hasRole: (bit: number) => boolean
  hasGeneralSubject: (bit: number) => boolean
  hasSubjectExpertise: (category: string, bit: number) => boolean
  hasMaskBit: (mask: CoreMaskField, bit: number) => boolean
}

const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserInfo | null>(null)
  const [isLoading, setIsLoading] = useState(true)

  useEffect(() => {
    const bootstrap = async () => {
      try {
        const token = sessionStorage.getItem('accessToken')
        if (token) {
          const { data } = await authApi.me()
          setUser(data)
          return
        }
        const { data } = await authApi.refresh()
        sessionStorage.setItem('accessToken', data.accessToken)
        setUser(data.user)
      } catch {
        sessionStorage.removeItem('accessToken')
        setUser(null)
      } finally {
        setIsLoading(false)
      }
    }
    void bootstrap()
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

  const checkMaskBit = useCallback(
    (mask: CoreMaskField, bit: number): boolean => hasMaskBit(user?.[mask], bit),
    [user]
  )

  const hasSubjectExpertise = useCallback(
    (category: string, bit: number): boolean =>
      hasMaskBit(user?.subjectExpertiseMasks?.[category], bit),
    [user]
  )

  const hasPermission = useCallback((bit: number) => checkMaskBit('permissionMask', bit), [checkMaskBit])
  const hasFeature = useCallback((bit: number) => checkMaskBit('featureMask', bit), [checkMaskBit])
  const hasRole = useCallback((bit: number) => checkMaskBit('roleMask', bit), [checkMaskBit])
  const hasGeneralSubject = useCallback((bit: number) => checkMaskBit('generalSubjectMask', bit), [checkMaskBit])

  return (
    <AuthContext.Provider
      value={{
        user,
        isLoading,
        login,
        register,
        logout,
        hasPermission,
        hasFeature,
        hasRole,
        hasGeneralSubject,
        hasSubjectExpertise,
        hasMaskBit: checkMaskBit,
      }}
    >
      {children}
    </AuthContext.Provider>
  )
}

export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used within AuthProvider')
  return ctx
}
