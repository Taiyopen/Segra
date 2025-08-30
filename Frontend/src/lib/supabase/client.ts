import { createClient } from '@supabase/supabase-js'

const SUPABASE_URL = "https://ponthqrnesnanivsatps.supabase.co"
const SUPABASE_ANON_KEY = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InBvbnRocXJuZXNuYW5pdnNhdHBzIiwicm9sZSI6ImFub24iLCJpYXQiOjE3Mzc2NzMzMjgsImV4cCI6MjA1MzI0OTMyOH0.k8pLDkDgKV0ZLjZjAZ6eUHa40rot5qWa7iJDQKWy1FA"

export const supabase = createClient(SUPABASE_URL, SUPABASE_ANON_KEY, {
  auth: {
    flowType: 'pkce',
    autoRefreshToken: true,
    persistSession: true,
    detectSessionInUrl: false
  }
})