import {StrictMode} from 'react';
import {createRoot} from 'react-dom/client';
import {QueryClient, QueryClientProvider} from '@tanstack/react-query';
import {supabase} from './lib/supabase/client';
import './globals.css';
import App from './App.tsx';
import {SelectedVideoProvider} from './Context/SelectedVideoContext.tsx';

// Create a React Query client
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 1000 * 60 * 5, // 5 minutes
      gcTime: 1000 * 60 * 30, // 30 minutes
    },
  },
});

// Initialize auth listener
supabase.auth.onAuthStateChange((event) => {
  if (event === 'SIGNED_OUT') {
    queryClient.clear();
  }
});

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <SelectedVideoProvider>
        <App />
      </SelectedVideoProvider>
    </QueryClientProvider>
  </StrictMode>,
);