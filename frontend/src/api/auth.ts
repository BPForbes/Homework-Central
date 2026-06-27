import axios from 'axios'
import type { AuthResponse, LoginRequest, RegisterRequest } from '../types/auth'

const AUTH_PATHS = ['/auth/login', '/auth/register', '/auth/refresh']

const api = axios.create({
  baseURL: '/api',
  withCredentials: true,
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
}

export default api
