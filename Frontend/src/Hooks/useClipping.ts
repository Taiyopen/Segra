import { useContext } from 'react';
import { ClippingContext } from '../Context/ClippingContext';

export function useClipping() {
    const context = useContext(ClippingContext);
    if (!context) {
        throw new Error('useClipping must be used within a ClippingProvider');
    }
    return context;
}
