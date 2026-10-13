export interface ExactAmount {
    numerator: bigint;
    denominator: bigint
}

const gcd = (left: bigint, right: bigint): bigint => right === 0n ? left : gcd(right, left % right);
const reduced = (numerator: bigint, denominator: bigint): ExactAmount => {
    if (denominator <= 0n) throw new Error('An exact amount needs a positive denominator.');
    const factor = gcd(numerator, denominator);
    return {numerator: numerator / factor, denominator: denominator / factor};
};

/** Parse authored decimal/fraction text without first passing through binary floating point. */
export function parseExactAmount(text: string): ExactAmount {
    if (typeof text !== 'string' || text.length > 256) throw new Error('Use an exact non-negative decimal or integer fraction.');
    const value = text.trim(), fraction = /^(\d+)\/(\d+)$/.exec(value);
    if (fraction) return reduced(BigInt(fraction[1]), BigInt(fraction[2]));
    const decimal = /^(\d+)(?:\.(\d+))?$/.exec(value);
    if (!decimal) throw new Error('Use an exact non-negative decimal or integer fraction. Empty fields remain incomplete.');
    const places = decimal[2] ?? '';
    return reduced(BigInt(decimal[1] + places), 10n ** BigInt(places.length));
}

export function exactInteger(text: string, multiplier = 1): number {
    if (!Number.isSafeInteger(multiplier) || multiplier <= 0) throw new Error('The payout unit needs a positive exact integer scale.');
    const amount = parseExactAmount(text), scaled = amount.numerator * BigInt(multiplier);
    if (scaled % amount.denominator !== 0n || scaled / amount.denominator > BigInt(Number.MAX_SAFE_INTEGER)) throw new Error('This executable lookup requires whole credits within the exact browser integer range.');
    return Number(scaled / amount.denominator);
}

/** Integer controls retain their original token until lexical/range checks have succeeded. */
export function exactIntegerInput(text: string, min = -Number.MAX_SAFE_INTEGER, max = Number.MAX_SAFE_INTEGER): number {
    if (typeof text !== 'string' || text.length > 256 || !/^-?\d+$/.test(text.trim())) throw new Error('Enter a complete whole number; empty and fractional inputs remain unapplied.');
    const integer = BigInt(text.trim());
    if (integer < BigInt(min) || integer > BigInt(max)) throw new Error(`Use a whole number from ${min} to ${max}.`);
    return Number(integer);
}

export function normalizedCredits(credits: number, roundCost: number): string {
    if (!Number.isSafeInteger(credits) || credits < 0 || !Number.isSafeInteger(roundCost) || roundCost <= 0) throw new Error('Credits and round cost must be exact supported integers.');
    const amount = reduced(BigInt(credits), BigInt(roundCost));
    return amount.denominator === 1n ? String(amount.numerator) : `${amount.numerator}/${amount.denominator}`;
}
