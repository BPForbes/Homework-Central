import { createContext, useCallback, useContext, useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { authApi } from '../api/authApi'
import type { CoreMaskField, UserInfo } from '../types/auth'
import type { CaptchaSubmission } from '../types/captcha'
import { hasMaskBit } from '../utils/bitmask'
import { clearAuthSession, getAccessToken, refreshSession, setAuthSession } from '../api/tokenManager'

interface AuthContextValue {
  user: UserInfo | null
  isLoading: boolean
  login: (email: string, password: string) => Promise<void>
  devLogin: (developerUserId: string, targetUserId: string | null, tenantDatabaseName: string | null) => Promise<void>
  register: (
    email: string,
    username: string,
    password: string,
    captcha?: CaptchaSubmission
  ) => Promise<void>
  logout: () => Promise<void>
  /** Re-fetches the current user — used after the dashboard "Verify" button changes roles. */
  refreshUser: () => Promise<void>
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
        const token = getAccessToken()
        if (token) {
          const { data } = await authApi.me()
          setUser(data)
          return
        }
        const data = await refreshSession()
        setUser(data.user)
      } catch {
        clearAuthSession()
        setUser(null)
      } finally {
        setIsLoading(false)
      }
    }
    void bootstrap()
  }, [])

  useEffect(() => {
    const handleExpired = () => setUser(null)
    document.addEventListener('hc:auth-expired', handleExpired)
    return () => document.removeEventListener('hc:auth-expired', handleExpired)
  }, [])

  const login = useCallback(async (email: string, password: string) => {
    const { data } = await authApi.login({ email, password })
    setAuthSession(data)
    setUser(data.user)
  }, [])

  /** Localhost dev bypass sign-in (see /devlogin). Empty targetUserId signs in as global DevAdmin. */
  const devLogin = useCallback(async (
    developerUserId: string,
    targetUserId: string | null,
    tenantDatabaseName: string | null,
  ) => {
    const { data } = await authApi.devLogin({
      developerUserId,
      targetUserId: targetUserId || undefined,
      tenantDatabaseName: tenantDatabaseName || undefined,
    })
    setAuthSession(data)
    setUser(data.user)
  }, [])

  const register = useCallback(
    async (email: string, username: string, password: string, captcha?: CaptchaSubmission) => {
      const { data } = await authApi.register({ email, username, password, captcha })
      setAuthSession(data)
      setUser(data.user)
    },
    []
  )

  const logout = useCallback(async () => {
    await authApi.logout().catch(() => {})
    clearAuthSession()
    setUser(null)
  }, [])

  const refreshUser = useCallback(async () => {
    const { data } = await authApi.me()
    setUser(data)
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
        devLogin,
        register,
        logout,
        refreshUser,
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
