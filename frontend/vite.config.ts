import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

function isConnectionRefused(error: unknown): boolean {
  if (!error || typeof error !== 'object') {
    return false
  }

  const code = 'code' in error ? String((error as { code?: unknown }).code) : ''
  return code === 'ECONNREFUSED' || code === 'ECONNRESET'
}

type ProxyServer = {
  on: (event: 'error', listener: (error: Error, req: unknown, res: unknown) => void) => void
}

type ProxyResponse = {
  writeHead?: (code: number, headers: Record<string, string>) => void
  end?: (body?: string) => void
  headersSent?: boolean
}

/** Avoid flooding the Vite log while the API process is still starting. */
function quietProxyConfigure(proxy: ProxyServer) {
  proxy.on('error', (error, _req, res) => {
    if (!isConnectionRefused(error)) {
      console.error('[vite] http proxy error:', error)
    }

    const response = res as ProxyResponse
    if (response.writeHead && response.end && !response.headersSent) {
      response.writeHead(502, { 'Content-Type': 'application/json' })
      response.end(JSON.stringify({ status: 'unreachable' }))
    }
  })
}

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
        configure: (proxy) => quietProxyConfigure(proxy),
      },
      '/hubs': {
        target: 'http://localhost:5000',
        changeOrigin: true,
        ws: true,
        configure: (proxy) => {
          proxy.on('error', (error: Error) => {
            if (!isConnectionRefused(error)) {
              console.error('[vite] ws proxy error:', error)
            }
          })
        },
      },
      '/healthz': {
        target: 'http://localhost:5000',
        changeOrigin: true,
        configure: (proxy) => quietProxyConfigure(proxy),
      },
    },
  },
})
