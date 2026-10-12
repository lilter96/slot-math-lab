import { useState } from 'react';
import type { ModelGraph } from '../../lib/modelProjection';
import type { StudioPatch } from '../../lib/authoring/studioEdits';
export interface StudioSymbol { id: string; name: string; kind?: string }
export interface StudioContext { graph: ModelGraph; preview: ModelGraph | null; symbols: StudioSymbol[]; assets: Record<string, string>; patches: StudioPatch[]; stage: (patches: StudioPatch[]) => void; disabled: boolean }
export function SymbolBadge({ symbol, asset, compact = false }: { symbol: StudioSymbol; asset?: string; compact?: boolean }) {
  const [failed, setFailed] = useState<string | null>(null);
  return <span className={'studio-symbol' + (compact ? ' compact' : '')}><span className={'symbol-art kind-' + (symbol.kind ?? 'Standard').toLowerCase()}>{asset && failed !== asset ? <img src={asset} alt="" onError={() => setFailed(asset)} /> : <span>{symbol.name.slice(0, 2)}</span>}</span>{!compact && <span className="symbol-caption"><b>{symbol.name}</b><small>ID {symbol.id}</small></span>}</span>;
}
