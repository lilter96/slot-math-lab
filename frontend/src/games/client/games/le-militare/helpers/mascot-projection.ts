export interface MascotProjectionInput {
  mascotX: number
  mascotY: number
  mascotScale: number
  launchPoint: { x: number; y: number }
  connectionPoint: { x: number; y: number }
  combatViewX: number
  combatViewY: number
  reelScale: number
}

export interface MascotProjectionOutput {
  launchLocalX: number
  launchLocalY: number
  connectionLocalX: number
  connectionLocalY: number
}

/**
 * Projects mascot launch and connection points from mascot-parent space into
 * CombatOperationView local (design) space.
 */
export function projectMascotPointsToCombatLocal(
  input: MascotProjectionInput,
): MascotProjectionOutput {
  const {
    mascotX,
    mascotY,
    mascotScale,
    launchPoint,
    connectionPoint,
    combatViewX,
    combatViewY,
    reelScale,
  } = input

  const glx = mascotX + launchPoint.x * mascotScale
  const gly = mascotY + launchPoint.y * mascotScale
  const gcx = mascotX + connectionPoint.x * mascotScale
  const gcy = mascotY + connectionPoint.y * mascotScale

  return {
    launchLocalX: (glx - combatViewX) / reelScale,
    launchLocalY: (gly - combatViewY) / reelScale,
    connectionLocalX: (gcx - combatViewX) / reelScale,
    connectionLocalY: (gcy - combatViewY) / reelScale,
  }
}
