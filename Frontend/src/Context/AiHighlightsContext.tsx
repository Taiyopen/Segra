import { createContext, useContext, ReactNode, useState, useEffect } from 'react';

interface AiProgress {
    id: number;
    progress: number;
    status: 'processing' | 'done';
    message: string;
}

interface AiHighlightsContextType {
    aiProgress: Record<number, AiProgress>;
    removeAiHighlight: (id: number) => void;
}

const AiHighlightsContext = createContext<AiHighlightsContextType | undefined>(undefined);

export function AiHighlightsProvider({ children }: { children: ReactNode }) {
    const [aiProgress, setAiProgress] = useState<Record<number, AiProgress>>({});

    useEffect(() => {
        const handleWebSocketMessage = (event: CustomEvent<{ method: string; content: any }>) => {
            const { method, content } = event.detail;
            
            if (method === 'AiProgress') {
                const progress = content as AiProgress;
                setAiProgress(prev => ({
                    ...prev,
                    [progress.id]: progress
                }));

                if (progress.status === 'done') {
                    setAiProgress(prev => {
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

    const removeAiHighlight = (id: number) => {
        setAiProgress(prev => {
            const { [id]: _, ...rest } = prev;
            return rest;
        });
    };

    return (
        <AiHighlightsContext.Provider value={{ aiProgress, removeAiHighlight }}>
            {children}
        </AiHighlightsContext.Provider>
    );
}

export function useAiHighlights() {
    const context = useContext(AiHighlightsContext);
    if (!context) {
        throw new Error('useAiHighlights must be used within an AiHighlightsProvider');
    }
    return context;
}
