import {at, gt, inSet, mapOver, num, only, state, toNumber, union, when} from '../expr';
import {Flow, Mechanics, type Mechanic, type Outcome} from '../flow';

/** One kind of card: the amount written on it and how likely a turned card is of this kind. */
export type PickCard = readonly [amount: number, weight: number];

/**
 * State the pick-bonus subgraphs read and write. `pickSeen[kind]` is 1 once a card of that kind has been turned;
 * `pickFaceDown` is the same list before any card is turned, and is never written.
 */
export const pickBonusState = (cards: readonly PickCard[]) => ({
    pickAmounts: cards.map(([amount]) => amount), pickFaceDown: cards.map(() => 0), pickSeen: cards.map(() => 0),
    pickSeenBits: 0, pickCard: '0', pickAward: 0, pickAwards: [], pickReveals: [],
});

/** One turned card: the award is its amount if a card of the same kind was turned before. */
function turnCard(cards: readonly PickCard[]): Mechanic {
    const kind = toNumber(state('pickCard'));
    return new Flow()
        .draw('card', 'Turn a card', 'pickCard', cards.map(([, weight], index): Outcome => [index, weight]))
        .write('match', 'A second card of the same kind wins its amount', 'pickAward', when(inSet(state('pickSeenBits'), kind), at('pickAmounts', kind), num(0)))
        .write('seen', 'Remember that this kind has been turned', 'pickSeenBits', union(state('pickSeenBits'), only(kind)))
        .record('trace-card', 'Remember the amount on the card', 'pickReveals', at('pickAmounts', kind))
        .mechanic('turn-card', 'Turns one card. The first amount to come up twice is the award.');
}

/**
 * A pick bonus: cards are turned one at a time until two show the same amount, which is left in `pickAward`.
 * With n kinds of card the pair is found in at most n + 1 turns. The loop is given one turn more than that: a
 * loop that uses up its whole limit is reported as cut short by the model, even when it ended by the rules.
 */
export function pickBonus(mechanics: Mechanics, cards: readonly PickCard[]): Mechanic {
    if (cards.length < 1 || cards.length > 64) throw new Error('A pick bonus accepts one to 64 card kinds');
    return new Flow()
        .write('reset', 'Start with no award', 'pickAward', num(0))
        .write('cover', 'Start with every card face down', 'pickSeenBits', num(0))
        .repeat('turn', 'Turn cards until two match', cards.length + 2, mechanics.add(turnCard(cards)), {
            until: gt(state('pickAward'), num(0)),
            bodyLabel: 'Turn a card'
        })
        .write('seen-cards', 'Which card kinds were turned', 'pickSeen',
            mapOver('pickFaceDown', when(inSet(state('pickSeenBits'), state('kind')), num(1), num(0)), {
                index: 'kind',
                itemType: 'Number'
            }))
        .record('trace-award', 'Remember the award', 'pickAwards', state('pickAward'))
        .mechanic('pick-bonus', 'Pick bonus: cards are turned until two show the same amount, and that amount is the award.');
}
