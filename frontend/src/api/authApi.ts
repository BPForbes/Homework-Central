import axios from 'axios'
import type { AuthResponse, LoginRequest, RegisterRequest } from '../types/auth'
import type { DevLoginOptions, DevLoginRequest } from '../types/devAuth'
import { configureApiClient } from './configureApiClient'

/** Auth endpoints excluded from the automatic 401 refresh retry loop. */
const AUTH_PATHS = ['/auth/login', '/auth/register', '/auth/refresh', '/auth/dev/login', '/auth/logout']

const api = axios.create({
  baseURL: '/api',
  withCredentials: true,
  // Fail fast when the API is still starting so auth bootstrap does not block the UI.
  timeout: 5000,
})

configureApiClient(api, AUTH_PATHS)

export const authApi = {
  login: (data: LoginRequest) => api.post<AuthResponse>('/auth/login', data),
  register: (data: RegisterRequest) => api.post<AuthResponse>('/auth/register', data),
  logout: () => api.post('/auth/logout'),
  refresh: () => api.post<AuthResponse>('/auth/refresh'),
  me: () => api.get<AuthResponse['user']>('/auth/me'),
  /** Probe whether localhost dev bypass endpoints are enabled on the API. */
  devStatus: () => api.get<import('../types/devAuth').DevStatus>('/auth/dev/status'),
  /** Fetch developer accounts and personas for the /devlogin dropdowns. */
  devOptions: () => api.get<DevLoginOptions>('/auth/dev/options', { timeout: 30000 }),
  /** Sign in via dev bypass; blank targetUserId signs in as DevAdmin on the server. */
  devLogin: (data: DevLoginRequest) => api.post<AuthResponse>('/auth/dev/login', data, { timeout: 30000 }),
}

export default api
