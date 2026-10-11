export type SymbolKind = 'regular' | 'wild' | 'scatter' | 'bonus'

export interface SymbolMeta {
  readonly id: number
  readonly name: string
  readonly kind: SymbolKind
}

export interface ThemePalette {
  readonly primary: number
  readonly accent: number
  readonly background: number
  readonly text: number
}

export type FeatureTag =
  | 'free-spins'
  | 'pick-bonus'
  | 'buy-bonus'
  | 'mystery'
  | 'cluster-pays'
  | 'cascade'
  | 'combat-operation'
  | 'hold-spin'
  | 'column-boost'

export interface WinTier {
  readonly thresholdX: number
  readonly copy: string
  readonly textureName?: string
}

export interface GameManifest {
  readonly gameId: string
  readonly displayName: string
  readonly grid: { readonly reels: number; readonly rows: number }
  readonly reelNaturalWidth: number
  readonly reelNaturalHeight: number
  readonly symbols: readonly SymbolMeta[]
  readonly symbolSize: number
  readonly theme: ThemePalette
  readonly winTiers: readonly WinTier[]
  readonly features: readonly FeatureTag[]
}

export interface RasterFrame {
  readonly x: number
  readonly y: number
  readonly width: number
  readonly height: number
}

/** The client loads raster images; sprite frames share one GPU texture source. */
export interface AssetManifest {
  readonly images?: Record<string, string>
  readonly atlases?: ReadonlyArray<{
    readonly image: string
    readonly frames: Readonly<Record<string, RasterFrame>>
  }>
  readonly audio?: Record<string, string>
}
