import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    globals: true,
  },
  server: {
    proxy: {
      '/queue':    { target: 'http://localhost:5000', changeOrigin: true },
      '/songs':    { target: 'http://localhost:5000', changeOrigin: true },
      '/presence': { target: 'http://localhost:5000', changeOrigin: true },
      '/stream':   { target: 'http://localhost:5000', changeOrigin: true },
      '/events':   { target: 'http://localhost:5000', changeOrigin: true },
    }
  },
})
