import { useRef, useEffect } from 'react';

export interface ConvergencePoint {
  n: number;
  rtp: number;
  stdErr: number;
}

interface ConvergenceChartProps {
  points: ConvergencePoint[];
  exactRtp?: number | null;
  width?: number;
  height?: number;
  theme?: {
    line?: string;
    faint?: string;
    exact?: string;
    sampled?: string;
    sampledDim?: string;
  };
}

function fmtN(n: number): string {
  if (n >= 1e6) return (n / 1e6).toFixed(0) + 'M';
  if (n >= 1e3) return (n / 1e3).toFixed(0) + 'k';
  return String(n);
}

export default function ConvergenceChart({
  points,
  exactRtp,
  width,
  height,
  theme = {},
}: ConvergenceChartProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);

  const {
    line = 'oklch(0.31 0.014 255)',
    faint = 'oklch(0.56 0.012 255)',
    exact = 'oklch(0.80 0.15 158)',
    sampled = 'oklch(0.74 0.135 250)',
    sampledDim = 'oklch(0.74 0.135 250 / 0.16)',
  } = theme;

  useEffect(() => {
    const cv = canvasRef.current;
    if (!cv) return;
    const dpr = window.devicePixelRatio || 1;
    const W = width ?? cv.clientWidth;
    const H = height ?? cv.clientHeight;
    const nw = Math.round(W * dpr);
    const nh = Math.round(H * dpr);
    cv.width = nw;
    cv.height = nh;
    const ctx = cv.getContext('2d');
    if (!ctx) return;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, W, H);

    if (W < 4 || H < 4 || points.length === 0) return;

    const padL = 52, padR = 16, padT = 14, padB = 28;
    const plotW = W - padL - padR, plotH = H - padT - padB;

    const yc = exactRtp ?? points[points.length - 1]?.rtp ?? 0.95;
    const yspan = Math.max(0.04, yc * 0.12);
    const ymin = yc - yspan, ymax = yc + yspan;
    const maxN = Math.max(1000, points[points.length - 1]?.n ?? 1000);

    const x = (n: number) => padL + (Math.log10(Math.max(100, n)) - 2) / (Math.log10(maxN) - 2 || 1) * plotW;
    const y = (v: number) => padT + (1 - (v - ymin) / (ymax - ymin)) * plotH;

    // Grid
    ctx.strokeStyle = line; ctx.fillStyle = faint; ctx.lineWidth = 0.5;
    ctx.font = "10px 'IBM Plex Mono', monospace"; ctx.textAlign = 'right'; ctx.textBaseline = 'middle';
    for (let i = 0; i <= 4; i++) {
      const v = ymin + (ymax - ymin) * i / 4;
      const yy = y(v);
      ctx.globalAlpha = 0.4;
      ctx.beginPath(); ctx.moveTo(padL, yy); ctx.lineTo(W - padR, yy); ctx.stroke();
      ctx.globalAlpha = 1;
      ctx.fillText((v * 100).toFixed(1) + '%', padL - 6, yy);
    }

    // X ticks
    ctx.textAlign = 'center'; ctx.textBaseline = 'top';
    [100, 1000, 10000, 100000, 1000000].forEach((n) => {
      if (n > maxN * 1.3) return;
      ctx.fillText(fmtN(n), x(n), H - padB + 6);
    });

    // CI band
    if (points.length > 1) {
      ctx.beginPath();
      points.forEach((p, i) => {
        const yy = y(p.rtp + 1.96 * p.stdErr);
        if (i) ctx.lineTo(x(p.n), yy); else ctx.moveTo(x(p.n), yy);
      });
      for (let i = points.length - 1; i >= 0; i--) {
        ctx.lineTo(x(points[i].n), y(points[i].rtp - 1.96 * points[i].stdErr));
      }
      ctx.closePath(); ctx.fillStyle = sampledDim; ctx.fill();
    }

    // Exact reference line
    if (exactRtp != null) {
      ctx.strokeStyle = exact; ctx.lineWidth = 1.5; ctx.setLineDash([5, 4]);
      ctx.beginPath(); ctx.moveTo(padL, y(exactRtp)); ctx.lineTo(W - padR, y(exactRtp));
      ctx.stroke(); ctx.setLineDash([]);
    }

    // Running RTP line
    if (points.length > 0) {
      ctx.strokeStyle = sampled; ctx.lineWidth = 1.8; ctx.beginPath();
      points.forEach((p, i) => {
        const xx = x(p.n), yy = y(Math.max(ymin, Math.min(ymax, p.rtp)));
        if (i) ctx.lineTo(xx, yy); else ctx.moveTo(xx, yy);
      });
      ctx.stroke();
      const last = points[points.length - 1];
      ctx.fillStyle = sampled;
      ctx.beginPath(); ctx.arc(x(last.n), y(Math.max(ymin, Math.min(ymax, last.rtp))), 3, 0, 7); ctx.fill();
    }
  }, [points, exactRtp, width, height, line, faint, exact, sampled, sampledDim]);

  return (
    <canvas
      ref={canvasRef}
      style={{ width: width ?? '100%', height: height ?? '100%', display: 'block' }}
    />
  );
}
