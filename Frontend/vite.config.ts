import { defineConfig, type UserConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { version as pkgVersion } from './package.json';
import tailwindcss from '@tailwindcss/vite';

// Packaged builds pass the real release version via SEGRA_VERSION so the frontend's __APP_VERSION__
// matches the backend's packaged version (used for "What's New" release-note gating). A bare local
// build has no SEGRA_VERSION and falls back to package.json ("Developer Preview").
const env = (globalThis as { process?: { env?: Record<string, string | undefined> } }).process?.env;
const version = env?.SEGRA_VERSION || pkgVersion;

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  // react-use-websocket exposes its hook via CJS `exports.default`; Rolldown's
  // stricter interop in Vite 8 returns undefined without this. Not yet in Vite's
  // published LegacyOptions typings — cast via UserConfig.
  legacy: {
    inconsistentCjsInterop: true,
  },
  server: {
    port: 2882,
  },
  define: {
    __APP_VERSION__: JSON.stringify(version),
  },
  build: {
    rollupOptions: {
      // Separate HTML entry so the PiP window never depends on query/hash surviving WebView2 Load().
      input: {
        main: 'index.html',
        monitoring: 'monitoring.html',
      },
      output: {
        entryFileNames: 'assets/[name].[hash].js',
        chunkFileNames: 'assets/[name].[hash].js',
        assetFileNames: 'assets/[name].[hash].[ext]',
        // Avoid aggressive manualChunks: Vite 8/Rolldown was nesting React inside
        // framer-motion, which blanked the Photino WebView (invalid hook / missing UI).
        manualChunks: (id) => {
          if (!id.includes('node_modules')) return;
          // Keep React in its own chunk so the monitoring.html MPA entry can resolve
          // StrictMode/createRoot correctly (Rolldown otherwise mis-wired shared exports).
          if (
            id.includes('node_modules/react-dom') ||
            id.includes('node_modules\\react-dom') ||
            id.includes('node_modules/react/') ||
            id.includes('node_modules\\react\\') ||
            id.includes('node_modules/scheduler') ||
            id.includes('node_modules\\scheduler')
          ) {
            return 'react';
          }
          if (id.includes('mp4box')) return 'mp4box';
          if (id.includes('lucide')) return 'lucide';
          if (id.includes('@tanstack')) return 'tanstack';
          return;
        },
      },
    },
    manifest: true,
  },
} as UserConfig);
