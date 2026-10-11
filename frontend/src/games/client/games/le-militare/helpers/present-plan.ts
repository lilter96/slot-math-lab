import type { LeMilitareResult } from '@tgslots/le-militare'

export interface PresentPlan {
  preAnnounce?: { text: string; ms: number }
  retriggerAnnounce?: { text: string; ms: number }
}

export function derivePresentPlan(result: LeMilitareResult): PresentPlan {
  switch (result.type) {
    case 'BASE':
      return result.triggeredFreeSpins
        ? { retriggerAnnounce: { text: 'FREE SPINS!', ms: 1500 } }
        : {}
    case 'FREE':
      return result.retriggered ? { retriggerAnnounce: { text: 'FREE SPINS!', ms: 1200 } } : {}
    case 'BUY':
      return {
        preAnnounce: { text: 'COMBAT OPERATION', ms: 1200 },
        retriggerAnnounce: { text: 'FREE SPINS!', ms: 1500 },
      }
  }
}
