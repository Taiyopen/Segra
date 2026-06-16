import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { version } from './package.json';
import tailwindcss from '@tailwindcss/vite';

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 2882,
  },
  define: {
    __APP_VERSION__: JSON.stringify(version),
  },
  build: {
    // Add cache busting for assets with content hashing
    rollupOptions: {
      output: {
        entryFileNames: 'assets/[name].[hash].js',
        chunkFileNames: 'assets/[name].[hash].js',
        assetFileNames: 'assets/[name].[hash].[ext]',
        manualChunks: (id) => {
          if (!id.includes('node_modules')) return;
          // Keep react + react-dom in one chunk to avoid circular vendor <-> react-dom splits.
          if (id.includes('/react-dom/') || id.includes('/react/') || id.includes('/scheduler/')) {
            return 'react';
          }
          if (id.includes('framer-motion')) return 'framer-motion';
          if (id.includes('mp4box')) return 'mp4box';
          if (id.includes('react-dnd')) return 'react-dnd';
          if (id.includes('lucide')) return 'lucide';
          if (id.includes('@tanstack')) return 'tanstack';
          return 'vendor';
        },
      },
    },
    // Ensure no caching issues by generating proper cache headers
    manifest: true,
  },
});
