import fs from 'node:fs'
import path from 'node:path'
import type { Rng } from '@tgslots/math/rng/types'

/** One number the original game asked its generator for: `value` out of `[lo, hi)`. */
export type Call = readonly [lo: number, hi: number, value: number]

/** Wraps a generator so that what the game asks of it can be read back, spin by spin. */
export function recording(base: Rng): { rng: Rng; take: () => Call[] } {
  let calls: Call[] = []
  return {
    rng: (lo, hi) => {
      const value = base(lo, hi)
      calls.push([lo, hi, value])
      return value
    },
    take: () => {
      const taken = calls
      calls = []
      return taken
    },
  }
}

/** Which outcome a number out of the total weight selects when the outcomes are laid end to end in order. */
export function outcomeAt(weights: readonly number[], value: number): number {
  let end = 0
  const index = weights.findIndex((weight) => value < (end += weight))
  if (index < 0) throw new Error(`${value} is beyond the total weight ${end}`)
  return index
}

/** Running mean of a quantity observed once per round, with the standard error of that mean. */
export class Mean {
  private count = 0
  private sum = 0
  private squares = 0
  add(value: number): void {
    this.count++
    this.sum += value
    this.squares += value * value
  }
  get value(): number {
    return this.sum / this.count
  }
  get stdErr(): number {
    return Math.sqrt((this.squares / this.count - this.value ** 2) / this.count)
  }
}

export function writeGolden(directory: string, name: string, golden: { rounds: unknown[]; simulation: unknown; [other: string]: unknown }): void {
  const file = path.join(directory, name)
  fs.writeFileSync(file, JSON.stringify(golden) + '\n')
  console.log(`${file}: ${golden.rounds.length} rounds`, golden.simulation)
}

/** One request to the original game server and what it answered: the result of the step and the session's state. */
export interface Exchange {
  action: string
  payload: Record<string, unknown>
  result: unknown
  state: unknown
}

/** One round the original server played: the graph that plays the same round, its draws, and the answers. */
export interface ServedRound {
  graph: string
  multiplier: number
  draws: number[]
  exchanges: Exchange[]
}

export function writeClientGolden(directory: string, name: string, source: string, rounds: ServedRound[]): void {
  const file = path.join(directory, name)
  fs.writeFileSync(file, JSON.stringify({ source, rounds }) + '\n')
  console.log(`${file}: ${rounds.length} rounds, ${rounds.reduce((sum, round) => sum + round.exchanges.length, 0)} answers`)
}
