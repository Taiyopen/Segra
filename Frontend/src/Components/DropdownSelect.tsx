import React, { useRef, useState } from 'react';
import { MdArrowDropDown } from 'react-icons/md';
import { motion } from 'framer-motion';

export interface DropdownItem {
  value: string;
  label: React.ReactNode;
}

interface DropdownSelectProps {
  items: DropdownItem[];
  value: string | undefined;
  onChange: (value: string) => void;
  placeholder?: React.ReactNode;
  buttonClassName?: string;
  menuClassName?: string;
  itemClassName?: string;
  disabled?: boolean;
  align?: 'start' | 'end';
  size?: 'sm' | 'md' | 'lg';
}

export default function DropdownSelect({
  items,
  value,
  onChange,
  placeholder = 'Select',
  buttonClassName,
  menuClassName = 'dropdown-content bg-base-300 border border-primary rounded-box z-[999] w-full p-2 mt-1 shadow overflow-x-hidden',
  itemClassName = 'block w-full text-left pl-2.5 pr-2.5 py-1.5 text-sm font-semibold hover:bg-white/5 active:!text-primary active:!bg-white/5 rounded-md transition-all duration-200 hover:pl-3.5 outline-none',
  disabled = false,
  align = 'end',
  size = 'md',
}: DropdownSelectProps) {
  const selected = items.find(i => i.value === value);
  const [isOpen, setIsOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const [openDirection, setOpenDirection] = useState<'down' | 'up'>('down');
  const [menuMaxHeight, setMenuMaxHeight] = useState<number | undefined>(undefined);

  const computeMenuFit = React.useCallback(() => {
    const btn = buttonRef.current;
    if (!btn) return;
    const rect = btn.getBoundingClientRect();
    const margin = 8; // spacing between button and menu
    const viewportH = window.innerHeight;
    const spaceBelow = Math.max(0, viewportH - rect.bottom - margin);
    const spaceAbove = Math.max(0, rect.top - margin);
    // Prefer down unless there's significantly more space above
    let dir: 'down' | 'up' = 'down';
    let available = spaceBelow;
    if (spaceBelow < 90 && spaceAbove > spaceBelow) {
      dir = 'up';
      available = spaceAbove;
    }
    setOpenDirection(dir);
    setMenuMaxHeight(Math.min(260, Math.max(120, Math.floor(available - 8))));
  }, []);

  // Recompute fit on window resize/scroll while open
  React.useEffect(() => {
    if (!isOpen) return;
    const handler = () => computeMenuFit();
    window.addEventListener('resize', handler, { passive: true } as any);
    window.addEventListener('scroll', handler, { passive: true } as any);
    return () => {
      window.removeEventListener('resize', handler as any);
      window.removeEventListener('scroll', handler as any);
    };
  }, [isOpen, computeMenuFit]);

  // Close on outside click or Escape, but rely on explicit isOpen for visibility
  React.useEffect(() => {
    function onDocMouseDown(e: MouseEvent) {
      if (!containerRef.current?.contains(e.target as Node)) setIsOpen(false);
    }
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape') setIsOpen(false);
    }
    document.addEventListener('mousedown', onDocMouseDown);
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('mousedown', onDocMouseDown);
      document.removeEventListener('keydown', onKeyDown);
    };
  }, []);

  return (
    <div
      ref={containerRef}
      className={`dropdown ${align === 'end' ? 'dropdown-end' : ''} ${openDirection === 'up' ? 'dropdown-top' : ''} ${isOpen ? 'dropdown-open' : ''}`}
    >
      <button
        tabIndex={0}
        className={`${(
          () => {
            if (buttonClassName) return buttonClassName;
            const sizeCls = size === 'sm' ? 'btn-sm px-3 py-0' : size === 'lg' ? 'btn-lg px-5 py-3' : 'px-4 py-2';
            return [
              'btn',
              'no-animation btn-secondary border border-primary hover:bg-base-200 hover:border-primary w-full justify-between bg-base-200',
              sizeCls,
            ].join(' ');
          }
        )()} ${disabled ? 'btn-disabled' : ''}`}
        disabled={disabled}
        ref={buttonRef}
        onMouseDown={(e) => {
          // Use mousedown to avoid immediate blur on mouseup
          e.preventDefault();
          if (disabled) return;
          if (!isOpen) {
            computeMenuFit();
            setIsOpen(true);
          } else {
            setIsOpen(false);
          }
        }}
        aria-expanded={isOpen}
        type="button"
      >
        <span className="flex-1 text-left truncate">
          {selected ? selected.label : placeholder}
        </span>
        <motion.span
          aria-hidden
          className="ml-2 inline-flex items-center justify-center"
          animate={{ rotate: isOpen ? 180 : 0 }}
          transition={{ type: 'spring', stiffness: 300, damping: 20 }}
        >
          <MdArrowDropDown size={26} />
        </motion.span>
      </button>
      <div
        tabIndex={0}
        ref={menuRef}
        className={menuClassName}
        style={menuMaxHeight ? { maxHeight: `${menuMaxHeight}px`, overflowY: 'auto' } : undefined}
      >
        <div className="space-y-1">
          {items.map((item) => (
            <button
              key={item.value}
              type="button"
              className={`${itemClassName} ${item.value === value ? '!text-primary' : 'text-white/80'}`}
              onClick={() => {
                if (disabled) return;
                onChange(item.value);
                setIsOpen(false);
                try {
                  (document.activeElement as HTMLElement)?.blur();
                } catch { /* no-op */ }
              }}
            >
              {item.label}
            </button>
          ))}
        </div>
      </div>
    </div>
  );
}
