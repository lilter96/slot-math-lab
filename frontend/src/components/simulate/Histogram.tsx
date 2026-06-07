import { useRef, useEffect } from 'react';

interface HistogramProps {
  data: Map<number, number>;
  width?: number;
  height?: number;
  theme?: { faint?: string; sampled?: string };
}

export default function Histogram({ data, width, height, theme = {} }: HistogramProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const { faint = 'oklch(0.56 0.012 255)', sampled = 'oklch(0.74 0.135 250)' } = theme;

  useEffect(() => {
    const cv = canvasRef.current;
    if (!cv) return;
    const dpr = window.devicePixelRatio || 1;
    const W = width ?? cv.clientWidth;
    const H = height ?? cv.clientHeight;
    cv.width = Math.round(W * dpr);
    cv.height = Math.round(H * dpr);
    const ctx = cv.getContext('2d');
    if (!ctx) return;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, W, H);

    const entries = Array.from(data.entries()).sort((a, b) => a[0] - b[0]);
    if (entries.length === 0 || W < 4 || H < 4) return;
    const padB = 24, padT = 8, padL = 8, padR = 8;
    const bw = (W - padL - padR) / entries.length;
    const maxC = Math.max(...entries.map((e) => e[1]));

    ctx.font = "9px 'IBM Plex Mono', monospace";
    ctx.textAlign = 'center';
    entries.forEach((e, i) => {
      const barH = (e[1] / maxC) * (H - padT - padB);
      const xx = padL + i * bw;
      ctx.fillStyle = e[0] === 0 ? faint : sampled;
      ctx.globalAlpha = e[0] === 0 ? 0.4 : 0.85;
      ctx.fillRect(xx + 1, H - padB - barH, bw - 2, barH);
      ctx.globalAlpha = 1;
      ctx.fillStyle = faint;
      const lab = e[0] === 0 ? '0' : e[0] + '×';
      if (i % Math.max(1, Math.ceil(entries.length / 8)) === 0 || i === entries.length - 1) {
        ctx.fillText(lab, xx + bw / 2, H - 4);
      }
    });
  }, [data, width, height, faint, sampled]);

  return (
    <canvas
      ref={canvasRef}
      style={{ width: width ?? '100%', height: height ?? '100%', display: 'block' }}
    />
  );
}
