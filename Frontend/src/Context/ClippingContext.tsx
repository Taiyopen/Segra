import { createContext, useContext, ReactNode, useState, useEffect } from 'react';

interface ClippingProgress {
    id: number;
    progress: number;
}

interface ClippingContextType {
    clippingProgress: Record<number, ClippingProgress>;
    removeClipping: (id: number) => void;
}

const ClippingContext = createContext<ClippingContextType | undefined>(undefined);

export function ClippingProvider({ children }: { children: ReactNode }) {
    const [clippingProgress, setClippingProgress] = useState<Record<number, ClippingProgress>>({});

    useEffect(() => {
        const handleWebSocketMessage = (event: CustomEvent<{ method: string; content: any }>) => {
            const { method, content } = event.detail;
            
            if (method === 'ClipProgress') {
                const progress = content as ClippingProgress;
                setClippingProgress(prev => ({
                    ...prev,
                    [progress.id]: progress
                }));

                if (progress.progress === 100) {
                   setClippingProgress(prev => {
                       const { [progress.id]: _, ...rest } = prev;
                       return rest;
                   });
                }
            }
        };

        window.addEventListener('websocket-message', handleWebSocketMessage as EventListener);
        return () => {
            window.removeEventListener('websocket-message', handleWebSocketMessage as EventListener);
        };
    }, []);

    const removeClipping = (id: number) => {
        setClippingProgress(prev => {
            const { [id]: _, ...rest } = prev;
            return rest;
        });
    };

    return (
        <ClippingContext.Provider value={{ clippingProgress, removeClipping }}>
            {children}
        </ClippingContext.Provider>
    );
}

export function useClipping() {
    const context = useContext(ClippingContext);
    if (!context) {
        throw new Error('useClipping must be used within a ClippingProvider');
    }
    return context;
}
