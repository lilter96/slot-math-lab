import { useEffect, useRef, useState } from 'react';

/** Match SVG coordinates to the rendered content width so tick labels retain
 * their physical font size when cards resize, including hidden/restored cards. */
export function usePlotWidth(initialWidth: number) {
  const ref = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(initialWidth);
  useEffect(() => {
    const element = ref.current;
    if (!element) return;
    const observer = new ResizeObserver(entries => {
      const measured = entries[0]?.contentRect.width;
      if (measured > 0) setWidth(Math.max(240, Math.floor(measured)));
    });
    observer.observe(element);
    return () => observer.disconnect();
  }, []);
  return { ref, width };
}
