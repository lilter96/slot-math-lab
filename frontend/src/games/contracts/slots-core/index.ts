/** What one unit of the bet multiplier costs, in credits. */
export class BetConfiguration {
  readonly baseCost: number;
  readonly lineCount: number;
  readonly costPerLine: number;
  readonly sideBetBase: number;

  constructor(baseCost: number, lineCount: number, costPerLine: number, sideBetBase: number) {
    this.baseCost = baseCost;
    this.lineCount = lineCount;
    this.costPerLine = costPerLine;
    this.sideBetBase = sideBetBase;
  }

  /** Every credit of the base cost is one line. */
  static fromLineCount(lineCount: number): BetConfiguration {
    return new BetConfiguration(lineCount, lineCount, 1, 0);
  }

  static fromBaseCost(baseCost: number): BetConfiguration {
    return new BetConfiguration(baseCost, baseCost, 1, 0);
  }
}

/** A bet: the game's cost taken `multiplier` times. */
export class Wager {
  readonly multiplier: number;
  readonly config: BetConfiguration;
  readonly creditsPerLine: number;
  readonly totalLineWager: number;
  readonly totalSideBet: number;
  readonly totalWager: number;

  constructor(multiplier: number, config: BetConfiguration) {
    if (!Number.isInteger(multiplier) || multiplier <= 0) throw new Error(`multiplier must be a positive integer: ${multiplier}`);
    this.multiplier = multiplier;
    this.config = config;
    this.creditsPerLine = config.costPerLine * multiplier;
    this.totalLineWager = this.creditsPerLine * config.lineCount;
    this.totalSideBet = config.sideBetBase * multiplier;
    this.totalWager = multiplier * config.baseCost;
  }
}

/** One winning payline. Payouts are in line credits at a bet multiplier of 1. */
export interface PaylineHit {
  readonly lineIndex: number;
  readonly symbolName: string;
  readonly matchCount: number;
  readonly basePayout: number;
  readonly wildMultiplier: number;
  readonly totalPayout: number;
}

/** One paying group of touching symbols. */
export interface ClusterHit {
  readonly symbolId: number;
  readonly symbolName: string;
  readonly size: number;
  readonly basePayout: number;
  readonly totalPayout: number;
  /** Board cells of the group: `reel * rowCount + row`. */
  readonly positions: readonly number[];
}

/** What every game's result of one step carries. */
export interface SpinResult {
  type: string;
  win: number;
  components?: Record<string, number>;
}
