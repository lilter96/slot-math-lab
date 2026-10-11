export interface GodRaySpec {
  points: string
  transform?: string
  opacity?: number
}

export interface RuneSpec {
  x: number
  y: number
  glyph: string
}

export interface VineSpec {
  d: string
  anchorX: number
  anchorY: number
}

export interface SceneDensities {
  fireflies: number
  embers: number
  dust: number
  leaves: number
}

export interface SceneSpec {
  viewBox: { width: number; height: number }
  bgTexture?: string
  hollowCenter: { x: number; y: number }
  hollowRx: number
  hollowRy: number
  godRays: GodRaySpec[]
  runes: RuneSpec[]
  vines: VineSpec[]
  mistCenter: { x: number; y: number }
  mistRx: number
  mistRy: number
  densities: SceneDensities
}

export const WW_SCENES: Record<'mobile' | 'tablet' | 'desktop', SceneSpec> = {
  mobile: {
    viewBox: { width: 390, height: 692 },
    bgTexture: 'BACKGROUND_9_16',
    hollowCenter: { x: 195, y: 305 },
    hollowRx: 14,
    hollowRy: 22,
    godRays: [
      { points: '150,0 175,0 230,460 200,460', transform: 'rotate(-3,180,0)' },
      { points: '185,0 198,0 245,420 220,420', opacity: 0.7 },
      { points: '215,0 230,0 270,440 245,440', transform: 'rotate(4,225,0)', opacity: 0.8 },
      { points: '95,0 110,0 165,400 140,400', transform: 'rotate(-7,105,0)', opacity: 0.55 },
      { points: '270,0 282,0 320,400 295,400', transform: 'rotate(6,275,0)', opacity: 0.55 },
    ],
    runes: [
      { x: 195, y: 240, glyph: '✦' },
      { x: 195, y: 380, glyph: '✦' },
      { x: 178, y: 340, glyph: '⟁' },
      { x: 213, y: 340, glyph: '⟁' },
    ],
    vines: [
      { d: 'M60,100 Q58,130 65,160 Q62,180 68,200', anchorX: 60, anchorY: 100 },
      { d: 'M150,110 Q148,140 155,170', anchorX: 150, anchorY: 110 },
      { d: 'M250,115 Q252,145 245,180 Q248,200 252,220', anchorX: 250, anchorY: 115 },
      { d: 'M340,100 Q342,130 335,160', anchorX: 340, anchorY: 100 },
    ],
    mistCenter: { x: 195, y: 430 },
    mistRx: 240,
    mistRy: 60,
    densities: { fireflies: 8, embers: 6, dust: 20, leaves: 2 },
  },

  tablet: {
    viewBox: { width: 1366, height: 1024 },
    bgTexture: 'BACKGROUND_4_3',
    hollowCenter: { x: 683, y: 465 },
    hollowRx: 26,
    hollowRy: 42,
    godRays: [
      { points: '500,0 530,0 620,640 580,640', transform: 'rotate(-3,510,0)' },
      { points: '600,0 620,0 700,620 670,620' },
      { points: '680,0 700,0 760,620 730,620', opacity: 0.85 },
      { points: '760,0 778,0 840,620 810,620', transform: 'rotate(3,765,0)', opacity: 0.75 },
      { points: '840,0 858,0 920,620 890,620', transform: 'rotate(5,845,0)', opacity: 0.65 },
      { points: '350,0 368,0 440,580 410,580', transform: 'rotate(-8,355,0)', opacity: 0.5 },
      { points: '1000,0 1018,0 1080,580 1050,580', transform: 'rotate(7,1005,0)', opacity: 0.5 },
    ],
    runes: [
      { x: 683, y: 355, glyph: '✦' },
      { x: 683, y: 580, glyph: '✦' },
      { x: 655, y: 515, glyph: '⟁' },
      { x: 711, y: 515, glyph: '⟁' },
      { x: 655, y: 410, glyph: '◈' },
      { x: 711, y: 410, glyph: '◈' },
    ],
    vines: [
      { d: 'M150,160 Q148,210 158,260 Q152,300 162,340', anchorX: 150, anchorY: 160 },
      { d: 'M350,170 Q348,220 358,270 Q352,310 362,350', anchorX: 350, anchorY: 170 },
      { d: 'M450,175 Q452,225 442,275', anchorX: 450, anchorY: 175 },
      { d: 'M900,170 Q902,220 892,270 Q898,310 888,350', anchorX: 900, anchorY: 170 },
      { d: 'M1000,160 Q998,210 1008,260', anchorX: 1000, anchorY: 160 },
      { d: 'M1200,170 Q1202,220 1192,270 Q1198,310 1188,350', anchorX: 1200, anchorY: 170 },
    ],
    mistCenter: { x: 683, y: 640 },
    mistRx: 800,
    mistRy: 100,
    densities: { fireflies: 18, embers: 14, dust: 50, leaves: 5 },
  },

  desktop: {
    viewBox: { width: 1920, height: 1080 },
    bgTexture: 'BACKGROUND_16_9',
    hollowCenter: { x: 960, y: 500 },
    hollowRx: 34,
    hollowRy: 54,
    godRays: [
      { points: '780,0 815,0 920,720 880,720', transform: 'rotate(-3,795,0)' },
      { points: '870,0 895,0 980,720 950,720', opacity: 0.9 },
      { points: '940,0 965,0 1030,720 1000,720' },
      { points: '1010,0 1035,0 1090,720 1060,720', opacity: 0.85 },
      { points: '1080,0 1105,0 1160,720 1130,720', transform: 'rotate(3,1090,0)', opacity: 0.75 },
      { points: '1170,0 1190,0 1240,680 1215,680', transform: 'rotate(5,1175,0)', opacity: 0.65 },
      { points: '660,0 678,0 740,650 710,650', transform: 'rotate(-7,665,0)', opacity: 0.55 },
      { points: '540,0 555,0 620,620 595,620', transform: 'rotate(-10,545,0)', opacity: 0.45 },
      { points: '1290,0 1305,0 1360,620 1335,620', transform: 'rotate(8,1295,0)', opacity: 0.45 },
    ],
    runes: [
      { x: 960, y: 370, glyph: '✦' },
      { x: 960, y: 640, glyph: '✦' },
      { x: 925, y: 565, glyph: '⟁' },
      { x: 996, y: 565, glyph: '⟁' },
      { x: 925, y: 430, glyph: '◈' },
      { x: 996, y: 430, glyph: '◈' },
    ],
    vines: [],
    mistCenter: { x: 960, y: 720 },
    mistRx: 1100,
    mistRy: 120,
    densities: { fireflies: 28, embers: 22, dust: 80, leaves: 8 },
  },
}

export function pickVariant(layout: {
  viewportClass: 'phone' | 'tablet' | 'desktop'
  orientation: 'portrait' | 'landscape'
}): 'mobile' | 'tablet' | 'desktop' {
  if (layout.viewportClass === 'phone') return 'mobile'
  if (layout.orientation === 'portrait')
    return layout.viewportClass === 'tablet' ? 'tablet' : 'mobile'
  return layout.viewportClass === 'desktop' ? 'desktop' : 'tablet'
}
