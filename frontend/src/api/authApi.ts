import axios from 'axios'
import type { AuthResponse, LoginRequest, RegisterRequest } from '../types/auth'

import type { DevLoginOptions, DevLoginRequest } from '../types/devAuth'

/** Auth endpoints excluded from the automatic 401 refresh retry loop. */
const AUTH_PATHS = ['/auth/login', '/auth/register', '/auth/refresh', '/auth/dev/login']

const api = axios.create({
  baseURL: '/api',
  withCredentials: true,
  // Fail fast when the API is still starting so auth bootstrap does not block the UI.
  timeout: 5000,
})

api.interceptors.request.use((config) => {
  const token = sessionStorage.getItem('accessToken')
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

api.interceptors.response.use(
  (response) => response,
  async (error) => {
    const original = error.config
    const url: string = original?.url ?? ''
    const isAuthEndpoint = AUTH_PATHS.some((p) => url.endsWith(p))

    if (error.response?.status === 401 && original && !original._retry && !isAuthEndpoint) {
      original._retry = true
      try {
        const { data } = await axios.post<AuthResponse>('/api/auth/refresh', null, {
          withCredentials: true,
        })
        sessionStorage.setItem('accessToken', data.accessToken)
        original.headers.Authorization = `Bearer ${data.accessToken}`
        return api(original)
      } catch {
        sessionStorage.removeItem('accessToken')
        window.location.href = '/login'
      }
    }
    return Promise.reject(error)
  }
)

export const authApi = {
  login: (data: LoginRequest) => api.post<AuthResponse>('/auth/login', data),
  register: (data: RegisterRequest) => api.post<AuthResponse>('/auth/register', data),
  logout: () => api.post('/auth/logout'),
  refresh: () => api.post<AuthResponse>('/auth/refresh'),
  me: () => api.get<AuthResponse['user']>('/auth/me'),
  /** Probe whether localhost dev bypass endpoints are enabled on the API. */
  devStatus: () => api.get<{ available: boolean }>('/auth/dev/status'),
  /** Fetch developer accounts and personas for the /devlogin dropdowns. */
  devOptions: () => api.get<DevLoginOptions>('/auth/dev/options'),
  /** Sign in via dev bypass; blank targetUserId signs in as DevAdmin on the server. */
  devLogin: (data: DevLoginRequest) => api.post<AuthResponse>('/auth/dev/login', data),
}

export default api
