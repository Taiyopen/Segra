import { HiOutlineSparkles } from 'react-icons/hi';

type TooltipSide = 'top' | 'right' | 'bottom' | 'left';

interface AiBadgeProps {
  tip?: string;
  side?: TooltipSide;
  className?: string;
  iconClassName?: string;
}

export default function AiBadge({
  tip = 'Powered by Segra AI',
  side = 'top',
  className = '',
  iconClassName = '',
}: AiBadgeProps) {
  return (
    <div
      className={`tooltip tooltip-${side} tooltip-primary inline-flex items-center ${className}`}
      data-tip={tip}
      aria-label={tip}
    >
      <HiOutlineSparkles className={`w-4 h-4 ml-0.5 text-purple-400 ${iconClassName}`} />
    </div>
  );
}
