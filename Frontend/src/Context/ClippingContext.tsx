import { createContext, useContext, useState, useEffect, type ReactNode } from 'react';
import { sendMessageToBackend } from '../Utils/MessageUtils';
import { useSelections } from './SelectionsContext';
import { useSettings } from './SettingsContext';
import { Selection } from '../Models/types';

export interface ClippingProgress {
    id: number;
    progress: number;
    selections: Selection[];
}

export interface ClippingContextType {
    clippingProgress: Record<number, ClippingProgress>;
    removeClipping: (id: number) => void;
    cancelClip: (id: number) => void;
}

export const ClippingContext = createContext<ClippingContextType | undefined>(undefined);

export function ClippingProvider({ children }: { children: ReactNode }) {
    const [clippingProgress, setClippingProgress] = useState<Record<number, ClippingProgress>>({});
    const { removeSelection } = useSelections();
    const settings = useSettings();

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
                    // If setting is enabled, remove all selections that were in the clip
                    if (settings.clipClearSelectionsAfterCreatingClip && progress.selections && progress.selections.length > 0) {
                        // Remove each selection that was included in the clip
                        progress.selections.forEach(selection => {
                            removeSelection(selection.id);
                        });
                    }

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
    }, [settings.clipClearSelectionsAfterCreatingClip, removeSelection]);

    const removeClipping = (id: number) => {
        setClippingProgress(prev => {
            const { [id]: _, ...rest } = prev;
            return rest;
        });
    };

    const cancelClip = (id: number) => {
        sendMessageToBackend('CancelClip', { id });
    };

    return (
        <ClippingContext.Provider value={{ clippingProgress, removeClipping, cancelClip }}>
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
