import {except, intersect, moved, mul, num, state, toText, union, type Expr} from './expr';

/** Exact integer literals, including words beyond JavaScript's decimal precision. */
const integer = (value: bigint): Expr => ({exprType: 'constant', kind: 'Integer', value: value.toString()});
const local = (name: string): Expr => ({exprType: 'local', name});
const bind = (name: string, value: Expr, body: Expr): Expr => ({exprType: 'let', name, value, body});
const xor = (left: Expr, right: Expr) => union(except(left, right), except(right, left));

/** A finite vector composed entirely from ordinary exact integer and finite-set expressions. */
export class PackedVector {
    readonly key: string;
    readonly length: number;
    readonly alphabetSize: number;
    readonly bits: number;
    readonly cellMask: bigint;
    readonly lowBits: bigint;
    readonly indicesKey: string;

    constructor(key: string, length: number, alphabetSize: number) {
        this.key = key;
        this.length = length;
        this.alphabetSize = alphabetSize;
        this.bits = Math.max(1, Math.ceil(Math.log2(alphabetSize)));
        if (!Number.isInteger(length) || length < 1 || !Number.isInteger(alphabetSize) || alphabetSize < 1 || length * this.bits > 63)
            throw new Error('A packed vector must contain positive dimensions and fit in 63 bits');
        this.cellMask = (1n << BigInt(this.bits)) - 1n;
        this.lowBits = Array.from({length}, (_, cell) => 1n << BigInt(cell * this.bits)).reduce((a, b) => a | b, 0n);
        this.indicesKey = `${key}Indices`;
    }

    /** Encode immutable authoring data, with no loss through floating-point serialization. */
    encode(values: readonly number[]): number {
        if (values.length > this.length) throw new Error('Packed authoring data exceeds the vector length');
        let word = 0n;
        for (let cell = 0; cell < values.length; cell++) {
            const value = values[cell];
            if (!Number.isInteger(value) || value < 0 || value >= this.alphabetSize) throw new Error('Packed symbols must be within the alphabet');
            word |= BigInt(value) << BigInt(cell * this.bits);
        }
        if (word > BigInt(Number.MAX_SAFE_INTEGER)) throw new Error('Authoring data word exceeds exact JSON-number precision');
        return Number(word);
    }

    read(position: Expr): Expr {
        return this.readOffset(mul(position, num(-this.bits)));
    }

    /** Read at an already projected signed bit offset, avoiding repeated coordinate arithmetic. */
    readOffset(offset: Expr): Expr {
        return intersect(moved(state(this.key), offset), integer(this.cellMask));
    }

    /** One low bit per equal cell. Nonselected bits cannot influence a cell's low bit. */
    matches(symbol: number): Expr {
        if (!Number.isInteger(symbol) || symbol < 0 || symbol >= this.alphabetSize) throw new Error('Packed symbol is outside the alphabet');
        const x = local('packedXor');
        return bind('packedXor', xor(state(this.key), integer(this.lowBits * BigInt(symbol))),
            except(integer(this.lowBits), union(...Array.from({length: this.bits}, (_, shift) => moved(x, -shift)))));
    }

    replace(symbol: number, replacement: Expr): Expr {
        const matches = local('packedMatches');
        return bind('packedMatches', this.matches(symbol), union(
            except(state(this.key), mul(matches, integer(this.cellMask))), mul(matches, replacement)));
    }

    decode(): Expr {
        return {
            exprType: 'map',
            stateKey: this.indicesKey,
            itemName: 'packedPosition',
            itemType: 'Number',
            maxItems: this.length,
            body: toText(this.read(state('packedPosition')))
        };
    }

    initialState() {
        return {[this.key]: 0, [this.indicesKey]: Array.from({length: this.length}, (_, cell) => cell)};
    }
}
