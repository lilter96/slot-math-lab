const gcd = (a: number, b: number): number => b === 0 ? a : gcd(b, a % b);

/**
 * Writes an amount of credits as an exact share of what the round costs, the unit every payout is reported in.
 * It is a decimal when the cost divides a power of ten ("0.20" of a bet of 25) and a fraction otherwise ("1/6"
 * of a bet of 30). The paytable written this way declares the precision of every win of the game.
 */
export function shareOf(cost: number): (credits: number) => string {
  if (!Number.isInteger(cost) || cost <= 0) throw new Error(`A round cannot cost ${cost}`);
  for (let places = 0, unit = 1; places <= 9; places++, unit *= 10)
    if (unit % cost === 0) return credits => (credits / cost).toFixed(places);
  return credits => {
    const common = gcd(credits, cost);
    return cost === common ? String(credits / common) : `${credits / common}/${cost / common}`;
  };
}
