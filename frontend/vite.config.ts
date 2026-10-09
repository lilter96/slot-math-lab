import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  preview: { proxy: { '/api': { target: 'http://localhost:5152', changeOrigin: true }, '/hubs': { target: 'http://localhost:5152', changeOrigin: true, ws: true } } },
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:5152',
        changeOrigin: true,
      },
      '/hubs': {
        target: 'http://localhost:5152',
        changeOrigin: true,
        ws: true,
      },
      '/openapi': {
        target: 'http://localhost:5152',
        changeOrigin: true,
      },
    },
  },
})
