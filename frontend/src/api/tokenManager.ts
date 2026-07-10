import axios from 'axios'
import type { AuthResponse } from '../types/auth'

const TOKEN_KEY = 'accessToken'
const EXPIRY_KEY = 'accessTokenExpiresAt'
const REFRESH_SKEW_MS = 60_000

let refreshPromise: Promise<AuthResponse> | null = null

async function requestRefresh(): Promise<AuthResponse> {
  const { data } = await axios.post<AuthResponse>('/api/auth/refresh', null, { withCredentials: true })
  setAuthSession(data)
  return data
}

function runRefreshLocked(): Promise<AuthResponse> {
  if ('locks' in navigator)
    return navigator.locks.request<AuthResponse>('hc-auth-refresh', async () => await requestRefresh())
  return requestRefresh()
}

export function getAccessToken(): string | null {
  return sessionStorage.getItem(TOKEN_KEY)
}

export function setAuthSession(response: AuthResponse): void {
  sessionStorage.setItem(TOKEN_KEY, response.accessToken)
  sessionStorage.setItem(EXPIRY_KEY, String(Date.now() + response.expiresIn * 1000))
  document.dispatchEvent(new CustomEvent('hc:auth-token-changed'))
}

export function clearAuthSession(): void {
  sessionStorage.removeItem(TOKEN_KEY)
  sessionStorage.removeItem(EXPIRY_KEY)
}

function tokenNeedsRefresh(): boolean {
  const expiresAt = Number(sessionStorage.getItem(EXPIRY_KEY))
  if (Number.isFinite(expiresAt) && expiresAt > 0)
    return Date.now() >= expiresAt - REFRESH_SKEW_MS

  const token = getAccessToken()
  if (!token)
    return true

  try {
    const payload = token.split('.')[1]
    const normalized = payload.replace(/-/g, '+').replace(/_/g, '/')
    const decoded = JSON.parse(atob(normalized.padEnd(Math.ceil(normalized.length / 4) * 4, '='))) as { exp?: number }
    return !decoded.exp || Date.now() >= decoded.exp * 1000 - REFRESH_SKEW_MS
  } catch {
    return true
  }
}

export function refreshSession(): Promise<AuthResponse> {
  if (!refreshPromise) {
    refreshPromise = runRefreshLocked()
      .catch((error: unknown) => {
        clearAuthSession()
        document.dispatchEvent(new CustomEvent('hc:auth-expired'))
        throw error
      })
      .finally(() => {
        refreshPromise = null
      })
  }

  return refreshPromise
}

export async function getFreshAccessToken(refreshIfMissing = true): Promise<string> {
  const current = getAccessToken()
  if (current && !tokenNeedsRefresh())
    return current
  if (!current && !refreshIfMissing)
    return ''

  return (await refreshSession()).accessToken
}
