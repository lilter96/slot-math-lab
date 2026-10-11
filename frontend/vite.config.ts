import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  preview: { proxy: { '/api': { target: process.env.SLOT_MATH_API_URL ?? 'http://localhost:5152', changeOrigin: true }, '/hubs': { target: process.env.SLOT_MATH_API_URL ?? 'http://localhost:5152', changeOrigin: true, ws: true } } },
  server: {
    proxy: {
      '/api': {
        target: process.env.SLOT_MATH_API_URL ?? 'http://localhost:5152',
        changeOrigin: true,
      },
      '/hubs': {
        target: process.env.SLOT_MATH_API_URL ?? 'http://localhost:5152',
        changeOrigin: true,
        ws: true,
      },
      '/openapi': {
        target: process.env.SLOT_MATH_API_URL ?? 'http://localhost:5152',
        changeOrigin: true,
      },
    },
  },
})
