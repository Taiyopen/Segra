import React, { useEffect, useState, useRef } from 'react';
import { HiOutlineSparkles } from 'react-icons/hi';
import { AiProgress } from '../Models/types';

interface AiContentCardProps {
  progress: AiProgress;
}

const AiContentCard: React.FC<AiContentCardProps> = ({ progress }) => {
  const [animatedProgress, setAnimatedProgress] = useState(progress.progress);
  const [displayedPercentage, setDisplayedPercentage] = useState(progress.progress);
  const animationFrameRef = useRef<number | null>(null);
  const isFirstRender = useRef(true);

  useEffect(() => {
    // Skip animation on first render
    if (isFirstRender.current) {
      isFirstRender.current = false;
      setAnimatedProgress(progress.progress);
      setDisplayedPercentage(progress.progress);
      return;
    }

    const timer = setTimeout(() => {
      setAnimatedProgress(progress.progress);

      const startValue = displayedPercentage;
      const endValue = progress.progress;
      const duration = 1200;
      const startTime = performance.now();

      if (animationFrameRef.current) {
        cancelAnimationFrame(animationFrameRef.current);
      }

      const animateCount = (timestamp: number) => {
        const elapsed = timestamp - startTime;
        const progress = Math.min(elapsed / duration, 1);

        const easeOutQuad = (t: number) => t * (2 - t);
        const easedProgress = easeOutQuad(progress);

        const currentValue = startValue + (endValue - startValue) * easedProgress;
        setDisplayedPercentage(Math.round(currentValue));

        if (progress < 1) {
          animationFrameRef.current = requestAnimationFrame(animateCount);
        }
      };

      animationFrameRef.current = requestAnimationFrame(animateCount);
    }, 50);

    return () => {
      clearTimeout(timer);
      if (animationFrameRef.current) {
        cancelAnimationFrame(animationFrameRef.current);
      }
    };
  }, [progress.progress, displayedPercentage]);

  return (
    <div className="card card-compact shadow-xl w-full relative highlight-card min-h-[271.5px]">
      <div className="absolute inset-0 rounded-lg highlight-border">
        <div className="card absolute inset-px bg-base-300 z-2">
          <figure className="relative aspect-w-16 aspect-h-9">
            <div
              className="w-full h-0 relative bg-base-300/70 rounded-none"
              style={{ paddingTop: '56.25%' }}
            >
              <div className="absolute inset-0 flex flex-col items-center justify-center bg-base-200">
                <HiOutlineSparkles className="w-12 h-12 text-purple-400 animate-pulse mb-2" />
                <p className="text-sm font-medium text-white/80">Generating AI Clip</p>
                <div className="mt-2 w-3/4">
                  <div className="w-full h-2 bg-gray-700 rounded-full overflow-hidden">
                    <div
                      className="h-full bg-white/80 rounded-full"
                      style={{
                        width: `${animatedProgress}%`,
                        transition: 'width 1.2s ease-out',
                      }}
                    ></div>
                  </div>
                  <p className="text-xs text-center mt-1 text-white/80">
                    <span>{displayedPercentage}%</span>
                  </p>
                </div>
              </div>
            </div>
          </figure>
          <div className="card-body text-gray-300">
            <h2 className="card-title">{progress.content.game}</h2>
            <p className="text-sm text-gray-400">{progress.message}</p>
          </div>
        </div>
      </div>
    </div>
  );
};

export default AiContentCard;
