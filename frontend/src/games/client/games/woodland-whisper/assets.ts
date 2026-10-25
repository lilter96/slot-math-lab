import featureFrames from './feature-frames.json' with { type: 'json' }
import type { AssetManifest } from '@tgslots/shared-contracts'

export const assets: AssetManifest = {
  images: {
    FEATURE_ATLAS: '/assets/images/woodland-whisper/feature-guardian.png',
    WIN_SMALL: '/assets/images/shared/win-panel.png',
    WIN_BIG: '/assets/images/shared/win-panel.png',
    WIN_MEGA: '/assets/images/shared/win-panel.png',
    BACKGROUND_16_9: '/assets/images/woodland-whisper/background-16-9.png',
    BACKGROUND_9_16: '/assets/images/woodland-whisper/background-9-16.png',
    BACKGROUND_4_3: '/assets/images/woodland-whisper/background-4-3.png',
    COIN: '/assets/images/woodland-whisper/coin-symbol.png',
    WOMAN: '/assets/images/woodland-whisper/woman-symbol.png',
    MUSHROOMS: '/assets/images/woodland-whisper/mushrooms-symbol.png',
    CHEST: '/assets/images/woodland-whisper/chest-symbol.png',
    TIARA: '/assets/images/woodland-whisper/tiara-symbol.png',
    PINECONE: '/assets/images/woodland-whisper/pinecone-symbol.png',
    REEL_FRAME: '/assets/images/woodland-whisper/reel-frame-thin.png',
    BONUS_CARD: '/assets/images/woodland-whisper/bonus-card.png',
    A: '/assets/images/woodland-whisper/a-symbol.png',
    K: '/assets/images/woodland-whisper/k-symbol.png',
    Q: '/assets/images/woodland-whisper/q-symbol.png',
    J: '/assets/images/woodland-whisper/j-symbol.png',
    '9': '/assets/images/woodland-whisper/9-symbol.png',
    '10': '/assets/images/woodland-whisper/10-symbol.png',
  },
  atlases: [{ image: 'FEATURE_ATLAS', frames: featureFrames }],
  audio: {
    'feature-rise': '/assets/sounds/original/feature-rise.mp3',
    'bgm-forest': '/assets/sounds/original/forest-afterglow.mp3',
    'spin-start': '/assets/sounds/original/spin-soft.mp3',
    'card-reveal': '/assets/sounds/original/card-reveal.mp3',
    'reel-stop': '/assets/sounds/original/reel-settle.mp3',
    'win-small': '/assets/sounds/original/win-soft.mp3',
    'win-big': '/assets/sounds/original/win-major.mp3',
  },
}
