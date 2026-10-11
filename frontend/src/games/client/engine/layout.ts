export type ViewportClass = 'phone' | 'tablet' | 'desktop'
export type OrientationMode = 'portrait' | 'landscape'
export type HUDLayoutMode = 'wide' | 'compact' | 'portrait'

export interface Point {
  x: number
  y: number
}

export interface Rect {
  x: number
  y: number
  width: number
  height: number
}

export interface UILayoutSnapshot {
  screenWidth: number
  screenHeight: number
  viewportClass: ViewportClass
  orientation: OrientationMode
  hudMode: HUDLayoutMode
  safePadding: number
  gutter: number
  gameplayArea: Rect
  reelBounds: Rect
  infoArea: Rect
  controlsArea: Rect
  modalBounds: Rect
  featureBounds: Rect
  overlayCenter: Point
  winOverlayScale: number
}

export const REEL_NATURAL_WIDTH = 780
export const REEL_NATURAL_HEIGHT = 420

function clamp(value: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, value))
}

export function fitScale(width: number, height: number, targetWidth: number, targetHeight: number) {
  if (width <= 0 || height <= 0) return 0
  return Math.min(width / targetWidth, height / targetHeight)
}

function getViewportClass(width: number, height: number): ViewportClass {
  const shortestSide = Math.min(width, height)
  const longestSide = Math.max(width, height)

  if (longestSide >= 1280 && shortestSide >= 720) return 'desktop'
  if (shortestSide < 600 || longestSide < 960) return 'phone'
  if (shortestSide < 960) return 'tablet'
  return 'desktop'
}

function getHudMode(
  width: number,
  height: number,
  viewportClass: ViewportClass,
  orientation: OrientationMode,
): HUDLayoutMode {
  if (orientation === 'portrait') return 'portrait'
  if (viewportClass === 'desktop' && width >= 1180 && width / height >= 1.45) return 'wide'
  return 'compact'
}

function makeRect(x: number, y: number, width: number, height: number): Rect {
  return {
    x,
    y,
    width: Math.max(0, width),
    height: Math.max(0, height),
  }
}

export function getResponsiveLayout(
  screenWidth: number,
  screenHeight: number,
  reelNaturalWidth = REEL_NATURAL_WIDTH,
  reelNaturalHeight = REEL_NATURAL_HEIGHT,
  headerInset = 0,
): UILayoutSnapshot {
  const orientation: OrientationMode = screenWidth >= screenHeight ? 'landscape' : 'portrait'
  const viewportClass = getViewportClass(screenWidth, screenHeight)
  const shortestSide = Math.min(screenWidth, screenHeight)
  const safePadding = clamp(Math.round(shortestSide * 0.03), 10, 28)
  const gutter = clamp(Math.round(shortestSide * 0.02), 6, 18)
  const hudMode = getHudMode(screenWidth, screenHeight, viewportClass, orientation)

  const infoHeight =
    hudMode === 'portrait'
      ? viewportClass === 'phone'
        ? 112
        : 124
      : hudMode === 'wide'
        ? 74
        : viewportClass === 'phone'
          ? 50
          : 62
  const controlsHeight =
    hudMode === 'portrait'
      ? viewportClass === 'phone'
        ? 96
        : 108
      : hudMode === 'wide'
        ? 108
        : viewportClass === 'phone'
          ? 78
          : 92
  const footerHeight = infoHeight + gutter + controlsHeight
  const footerTop = screenHeight - safePadding - footerHeight

  const infoArea = makeRect(safePadding, footerTop, screenWidth - safePadding * 2, infoHeight)
  const controlsArea = makeRect(
    safePadding,
    infoArea.y + infoArea.height + gutter,
    screenWidth - safePadding * 2,
    controlsHeight,
  )
  const gameplayArea = makeRect(
    safePadding,
    safePadding + headerInset,
    screenWidth - safePadding * 2,
    footerTop - safePadding - gutter - headerInset,
  )

  const reelScale = Math.min(
    1,
    fitScale(gameplayArea.width, gameplayArea.height, reelNaturalWidth, reelNaturalHeight),
  )
  const reelWidth = reelNaturalWidth * reelScale
  const reelHeight = reelNaturalHeight * reelScale
  const reelBounds = makeRect(
    gameplayArea.x + (gameplayArea.width - reelWidth) / 2,
    gameplayArea.y + (gameplayArea.height - reelHeight) / 2,
    reelWidth,
    reelHeight,
  )

  const modalHorizontalInset = hudMode === 'portrait' ? safePadding : safePadding * 2
  const modalVerticalInset = hudMode === 'compact' ? safePadding : safePadding * 2
  const modalBounds = makeRect(
    modalHorizontalInset,
    modalVerticalInset + headerInset,
    screenWidth - modalHorizontalInset * 2,
    screenHeight - modalVerticalInset * 2 - headerInset,
  )
  const featureBounds = makeRect(
    safePadding,
    safePadding + headerInset,
    screenWidth - safePadding * 2,
    screenHeight - safePadding * 2 - headerInset,
  )

  return {
    screenWidth,
    screenHeight,
    viewportClass,
    orientation,
    hudMode,
    safePadding,
    gutter,
    gameplayArea,
    reelBounds,
    infoArea,
    controlsArea,
    modalBounds,
    featureBounds,
    overlayCenter: {
      x: reelBounds.x + reelBounds.width / 2,
      y: reelBounds.y + reelBounds.height / 2,
    },
    winOverlayScale: clamp(reelBounds.width / reelNaturalWidth, 0.52, 1),
  }
}
