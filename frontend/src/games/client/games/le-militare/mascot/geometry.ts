import {
  LAUNCHER_HINGE_X,
  LAUNCHER_HINGE_Y,
  LAUNCHER_LEN,
  CONNECTION_ANCHOR_X,
  CONNECTION_ANCHOR_Y,
} from './design.js'

/** Missile launch point in the mascot's local space. */
export function computeLaunchPoint(
  masterX: number,
  masterY: number,
  isDeployed = true,
): { x: number; y: number } {
  if (isDeployed) {
    return {
      x: masterX + LAUNCHER_HINGE_X,
      y: masterY + LAUNCHER_HINGE_Y - LAUNCHER_LEN,
    }
  }
  // Undeployed: launcher points right
  return {
    x: masterX + LAUNCHER_HINGE_X + LAUNCHER_LEN,
    y: masterY + LAUNCHER_HINGE_Y,
  }
}

/** Wire connection point at the hull underbody in the mascot's local space. */
export function computeConnectionPoint(masterX: number, masterY: number): { x: number; y: number } {
  return {
    x: masterX + CONNECTION_ANCHOR_X,
    y: masterY + CONNECTION_ANCHOR_Y,
  }
}
