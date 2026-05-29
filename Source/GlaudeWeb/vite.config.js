import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      // Binance Spot Testnet REST
      '/bapi': {
        target: 'https://testnet.binance.vision',
        changeOrigin: true,
        rewrite: path => path.replace(/^\/bapi/, ''),
      },
    },
  },
  build: {
    outDir: 'dist',
    sourcemap: true,
  },
})
