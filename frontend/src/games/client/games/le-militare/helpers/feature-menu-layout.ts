/** Keep feature choices readable and their buy targets at least 44px on phones. */
export function getFeatureMenuLayout(width: number, height: number) {
  const portrait = width < 600 && height > width
  const landscape = width >= 600 && height < 500
  const compact = portrait || landscape
  const columns = portrait ? 2 : 5
  const panelWidth = portrait
    ? Math.min(350, width - 24)
    : landscape
      ? Math.min(980, width - 24)
      : 980
  const padding = compact ? 12 : 32
  const gap = compact ? 12 : 16
  const cardHeight = portrait ? 146 : landscape ? 180 : 300
  const cardsTop = portrait ? 184 : landscape ? 128 : 252
  const rows = Math.ceil(5 / columns)
  const panelHeight = cardsTop + rows * cardHeight + (rows - 1) * gap + padding
  const scale = Math.min(1, (width - 24) / panelWidth, (height - 24) / panelHeight)
  return {
    portrait: compact,
    landscape,
    columns,
    panelWidth,
    padding,
    gap,
    cardHeight,
    cardsTop,
    panelHeight,
    scale,
    cardWidth: (panelWidth - padding * 2 - gap * (columns - 1)) / columns,
  }
}
