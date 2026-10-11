import { fileURLToPath } from 'node:url'
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

const source = (path: string) => fileURLToPath(new URL(path, import.meta.url))

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  resolve: {
    // The ported slot game client (src/games/client) imports the packages of its original workspace by name.
    alias: [{ find: /^@tgslots\//, replacement: source('./src/games/contracts/') }],
    dedupe: ['pixi.js', 'gsap'],
  },
  // Two pages: the lab itself and the slot games played on its graphs.
  build: { rollupOptions: { input: { main: source('./index.html'), games: source('./games.html') } } },
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
