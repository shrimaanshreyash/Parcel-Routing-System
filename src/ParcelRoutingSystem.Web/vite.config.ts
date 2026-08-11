import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

/**
 * Keeps the browser on one origin during development while forwarding only the
 * explicit API and health paths to ASP.NET Core. Production uses the hosting
 * platform's equivalent same-origin reverse proxy.
 */
export default defineConfig({
  plugins: [react()],
  server: {
    host: '127.0.0.1',
    port: 8190,
    strictPort: true,
    proxy: {
      '/api': 'http://127.0.0.1:5080',
      '/health': 'http://127.0.0.1:5080',
    },
    headers: {
      'X-Content-Type-Options': 'nosniff',
      'X-Frame-Options': 'DENY',
      'Referrer-Policy': 'no-referrer',
      'Permissions-Policy': 'camera=(), microphone=(), geolocation=()',
    },
  },
})
