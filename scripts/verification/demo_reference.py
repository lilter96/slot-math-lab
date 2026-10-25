#!/usr/bin/env python3
"""Independent Nine Lives rule oracle. Reads JSON data; never loads product code.

Fraction is used for payouts and stake conversion. The documented PRNG/alias
contract uses binary64 thresholds, matching seeded replay rather than claiming
an exact RTP proof. This program does not interpret graph expressions.
"""

import argparse
from collections import deque
from fractions import Fraction
from itertools import product
import json
from pathlib import Path


MASK = (1 << 64) - 1
GAMMA = 0x9E3779B97F4A7C15


def mix(value):
    value = ((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9) & MASK
    value = ((value ^ (value >> 27)) * 0x94D049BB133111EB) & MASK
    return value ^ (value >> 31)


def rotate(value, bits):
    return ((value << bits) | (value >> (64 - bits))) & MASK


class Random:
    def __init__(self, seed, index):
        expansion = mix((seed + (index + 1) * GAMMA) & MASK)
        self.state = [mix((expansion + (i + 1) * GAMMA) & MASK) for i in range(4)]

    def word(self):
        a, b, c, d = self.state
        result = (rotate((b * 5) & MASK, 7) * 9) & MASK
        t = (b << 17) & MASK
        c ^= a
        d ^= b
        b ^= c
        a ^= d
        c ^= t
        self.state = [a, b, c, rotate(d, 45)]
        return result

    def bucket(self, count):
        mask = (1 << ((count - 1) | 1).bit_length()) - 1
        while True:
            value = self.word() & mask
            if value < count:
                return value


class Draw:
    def __init__(self, entries):
        self.outcomes = [int(entry['outcomeId']) for entry in entries]
        weights = [entry['weight'] for entry in entries]
        size = len(weights)
        total = sum(weights)
        scaled = [weight * size / total for weight in weights]
        small = deque(i for i in range(size) if scaled[i] < 1)
        large = deque(i for i in range(size) if scaled[i] >= 1)
        self.thresholds = [1.0] * size
        self.aliases = list(range(size))
        while small and large:
            low, high = small.popleft(), large.popleft()
            self.thresholds[low] = max(0.0, scaled[low])
            self.aliases[low] = high
            scaled[high] -= 1.0 - scaled[low]
            (small if scaled[high] < 1 else large).append(high)

    def __call__(self, random):
        bucket = random.bucket(len(self.outcomes))
        fraction = (random.word() >> 11) / (1 << 53)
        return self.outcomes[bucket if fraction < self.thresholds[bucket] else self.aliases[bucket]]


class Reference:
    def __init__(self, graph):
        self.rows = graph['boardConfig']['rows']
        self.cols = graph['boardConfig']['columns']
        self.order = [row * self.cols + col for col in range(self.cols) for row in range(self.rows)]
        self.base_cost = graph['initialState']['baseCost']
        self.cap = Fraction(graph['initialState']['capCoins'])
        self.max_multiplier = graph['initialState']['maxMultiplier']
        self.max_cascades = graph['initialState']['maxCascades']
        self.free_spins = graph['initialState']['freeSpins']
        self.entry_cost = graph['initialState']['entryCost']
        self.paytable = {int(entry['symbolId']): {count: Fraction(pay) * self.base_cost for count, pay in zip(entry['counts'], entry['payouts'])}
                         for entry in graph['paytables'][0]['entries']}

        def draw(mechanic, node):
            return Draw(next(n for n in graph['mechanics'][mechanic]['nodes'] if n['id'] == node)['drawWeights'])

        self.base = draw('base-spin', 'cell-0')
        self.free = draw('free-spin', 'cell-0')
        self.prize = draw('free-spin', 'prize-0')
        self.refills = [draw(f'refill-row-{row}', 'symbol') for row in range(self.rows)]

    def evaluate(self, board):
        swept, claimed = set(), set()
        hits = []
        for first in self.order:
            symbol = board[first]
            if symbol not in self.paytable or symbol in swept:
                continue
            swept.add(symbol)
            visited = set()
            for start in self.order:
                if board[start] != symbol or start in visited:
                    continue
                queue = [start]
                visited.add(start)
                for position in queue:
                    row, col = divmod(position, self.cols)
                    neighbors = []
                    if row > 0:
                        neighbors.append(position - self.cols)
                    if row + 1 < self.rows:
                        neighbors.append(position + self.cols)
                    if col > 0:
                        neighbors.append(position - 1)
                    if col + 1 < self.cols:
                        neighbors.append(position + 1)
                    for candidate in neighbors:
                        if candidate not in visited and (board[candidate] == symbol or board[candidate] == 0 and candidate not in claimed):
                            visited.add(candidate)
                            queue.append(candidate)
                payout = self.paytable[symbol].get(len(queue), Fraction(0))
                if payout > 0:
                    claimed.update(p for p in queue if board[p] == 0)
                    hits.append({'symbol': symbol, 'size': len(queue), 'payout': int(payout), 'positions': queue})
        return hits

    def round(self, seed, index, buy=False):
        random = Random(seed, index)
        total = Fraction(0)
        multiplier = 1
        spins, steps = [], []
        base_total = Fraction(0)
        free_total = Fraction(0)
        scatters = 0

        def spin(free):
            nonlocal total, multiplier, scatters, base_total, free_total
            if not free:
                multiplier = 1
            board = [0] * len(self.order)
            for p in self.order:
                board[p] = (self.free if free else self.base)(random)
            initial = board.copy()
            coins = []
            collection = Fraction(0)
            cluster = Fraction(0)
            if free:
                prizes = [0] * len(self.order)
                for p in self.order:
                    prizes[p] = self.prize(random)
                coins = [{'position': p, 'value': prizes[p] * self.base_cost} for p in range(len(board)) if board[p] == 6]
                collection = min(sum((Fraction(c['value']) for c in coins), Fraction(0)) * multiplier, self.cap - total)
                total += collection
                board = [0 if symbol == 6 else symbol for symbol in board]
            else:
                scatters = board.count(7)
            for _ in range(self.max_cascades):
                hits = self.evaluate(board)
                if not hits:
                    break
                before = board.copy()
                award = min(sum((Fraction(h['payout']) for h in hits), Fraction(0)) * multiplier, self.cap - total)
                total += award
                cluster += award
                vanished = set(p for hit in hits for p in hit['positions'])
                winning_types = {hit['symbol'] for hit in hits}
                vanished.update(p for p in self.order if before[p] in winning_types)
                for col in range(self.cols):
                    survivors = [before[row * self.cols + col] for row in range(self.rows) if row * self.cols + col not in vanished]
                    for row in range(self.rows - 1, -1, -1):
                        board[row * self.cols + col] = survivors.pop() if survivors else self.refills[self.rows - 1 - row](random)
                steps.append({'spin': len(spins), 'before': before, 'after': board.copy(), 'hits': hits, 'multiplier': multiplier, 'win': int(award)})
                multiplier = min(self.max_multiplier, multiplier + 1)
            if not free:
                multiplier = 1
            spins.append({'board': initial, 'final': board, 'coins': coins, 'collection': int(collection), 'cluster': int(cluster),
                          'win': int(collection + cluster), 'multiplier': multiplier, 'roundWin': int(total)})
            if free:
                free_total += collection + cluster
            else:
                base_total += collection + cluster

        if not buy:
            spin(False)
        triggered = buy or scatters >= 4 and total < self.cap
        if triggered:
            multiplier = 1
            for _ in range(self.free_spins):
                if total >= self.cap:
                    break
                spin(True)
        return {'seed': seed, 'index': index, 'buy': buy, 'win': str(total / self.base_cost), 'baseCoins': int(base_total), 'freeCoins': int(free_total),
                'scatters': scatters, 'bonusTriggered': triggered, 'spins': spins, 'steps': steps}


def main():
    root = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--model', type=Path, default=root / 'backend/SlotMath.Core.Tests/TestData/LabDemo/lab-demo.json')
    parser.add_argument('--output', type=Path, default=root / 'backend/SlotMath.Core.Tests/TestData/LabDemo/lab-demo-reference.json')
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    reference = Reference(json.loads(args.model.read_text()))
    rounds = [reference.round(42, i, buy) for buy in [False, True] for i in [*range(10), 2376]]
    boards = [[0] * 30, [7] * 30, [1] * 30, [1, 1, 1, 1, 0, 7, 2, 2, 2, 2, 2, 7] + [7] * 18, [1, 1, 1, 0, 7, 7, 2, 2, 2, 2, 7, 7] + [7] * 18]
    # Exhaust the six-cell region, including shared Wilds, exact threshold,
    # disconnected short components and every first-occurrence symbol order.
    for symbols in product([0, 1, 2], repeat=6):
        board = [7] * 30
        for position, symbol in zip([0, 1, 2, 6, 7, 8], symbols):
            board[position] = symbol
        boards.append(board)
    # Independent weighted boards exercise the full geometry and both densities.
    for draw in [reference.base, reference.free]:
        for index in range(64):
            random = Random(173, index)
            board = [0] * 30
            for position in reference.order:
                board[position] = draw(random)
            boards.append(board)
    components = [{'board': board, 'hits': reference.evaluate(board)} for board in boards]
    text = json.dumps({'components': components, 'provenance': 'Independent rule verification; this is not an Exact RTP proof.', 'rounds': rounds}, indent=2) + '\n'
    if args.check:
        if args.output.read_text() != text:
            raise SystemExit('Independent reference fixture is stale.')
    else:
        args.output.write_text(text)
    print('Independent Fraction reference ' + ('verified.' if args.check else 'exported.'))


if __name__ == '__main__':
    main()
