import {useState, useEffect} from 'react';
import {Session} from '@supabase/supabase-js';
import {supabase} from '../lib/supabase/client';
import {sendMessageToBackend} from '../Utils/MessageUtils';

export function useAuth() {
  const [session, setSession] = useState<Session | null>(null);
  const [user, setUser] = useState(session?.user ?? null);

  useEffect(() => {
    const {data: authListener} = supabase.auth.onAuthStateChange(
      (event, session) => {
        setSession(session);
        setUser(session?.user ?? null);

        if (event === 'SIGNED_OUT') {
          sendMessageToBackend('Logout');
        }
      }
    );

    return () => authListener?.subscription.unsubscribe();
  }, []);

  return {user, session};
}