import type { AxiosError, AxiosInstance, InternalAxiosRequestConfig } from 'axios'
import { emitApiMutation } from './apiActivity'
import { getAccessToken, getFreshAccessToken, refreshSession } from './tokenManager'

interface RetryableConfig extends InternalAxiosRequestConfig {
  _retry?: boolean
}

export function configureApiClient(api: AxiosInstance, authPaths: string[] = []): void {
  api.interceptors.request.use(async (config) => {
    emitApiMutation(config.method, config.url)

    const current = getAccessToken()
    if (current) {
      const token = await getFreshAccessToken(false)
      if (token)
        config.headers.Authorization = `Bearer ${token}`
    }
    return config
  })

  api.interceptors.response.use(
    (response) => response,
    async (error: AxiosError) => {
      const original = error.config as RetryableConfig | undefined
      const url = original?.url ?? ''
      const isAuthEndpoint = authPaths.some((path) => url.endsWith(path))

      if (error.response?.status === 401 && original && !original._retry && !isAuthEndpoint) {
        original._retry = true
        const { accessToken } = await refreshSession()
        original.headers.Authorization = `Bearer ${accessToken}`
        return api(original)
      }

      return Promise.reject(error)
    },
  )
}
