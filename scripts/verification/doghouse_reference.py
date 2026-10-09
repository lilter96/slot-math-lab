#!/usr/bin/env python3
"""Independent rule oracle, for verification only. Does not import or execute production payout code."""
from fractions import Fraction as F
from pathlib import Path
import json
import argparse
ROOT = Path(__file__).resolve().parents[2]
graph = json.loads((ROOT / 'backend/SlotMath.Core.Tests/TestData/DogHouse/dog-house-ui.json').read_text())
state = graph['initialState']
base = [list(map(int, state[f'baseReel{i}'][:-2])) for i in range(5)]
free = [list(map(int, state[f'freeReel{i}'][:-2])) for i in range(5)]
paytable = list(map(int, state['linePaytable']))

def multiplier(prefix, col):
    draw = next(n for n in graph['mechanics'][f'dog-{prefix}-spin']['nodes'] if n['id'] == f'wild-{col}')
    return F(sum(int(x['outcomeId']) * x['weight'] for x in draw['drawWeights']), sum(x['weight'] for x in draw['drawWeights']))

def expected_spin(reels, turn, sticky=False, free_mean=None):
    """Independent per-line rule derivation: probability and first moment of additive Wild values."""
    probability = []
    for reel in reels:
        natural_wild = F(reel.count(2), len(reel))
        survival = (1-natural_wild) ** (turn-1) if sticky else F(1)
        p = {symbol: F(reel.count(symbol), len(reel)) * survival for symbol in range(1, 14)}
        if sticky: p[2] = 1 - (1-natural_wild) ** turn
        probability.append(p)
    expectation = F(0)
    for symbol in range(3,14):
        matching = probability[0][symbol]
        no_wild = matching
        weighted_factor = F(0)
        for col in range(1,5):
            ordinary, wild = probability[col][symbol], probability[col][2]
            match = ordinary + wild
            mean = free_mean if sticky and free_mean is not None else multiplier('free' if sticky else 'base', col) if col < 4 else 0
            weighted_factor = weighted_factor * match + matching * wild * mean
            no_wild *= ordinary
            matching *= match
            count = col+1
            if count >= 3:
                terminates = 1 if col == 4 else 1 - probability[col+1][symbol] - probability[col+1][2]
                expectation += paytable[(symbol-1)*3+count-3] * (weighted_factor + no_wild) * terminates
    return expectation # 20 equal row-marginal lines / 20 stake coins

trigger = F(1)
for col in [0,2,4]:
    reel = base[col]
    windows = sum(any(reel[(stop+row)%len(reel)] == 1 for row in range(3)) for stop in range(len(reel)))
    trigger *= F(windows, len(reel))
bonus_draw = next(n for n in graph['nodes'] if n['id'] == 'bonus-cell')['drawWeights']
cell_prob = {int(x['outcomeId']): F(x['weight'],sum(y['weight'] for y in bonus_draw)) for x in bonus_draw}
pmf = {0:F(1)}
for _ in range(9):
    after = {}
    for total, p in pmf.items():
        for value, q in cell_prob.items(): after[total+value] = after.get(total+value,F(0)) + p*q
    pmf = after
assert sum(pmf.values()) == 1
def bonus_expectation(mean=None):
    cumulative = F(0); conditional = F(0)
    for spin in range(1,28):
        cumulative += expected_spin(free,spin,True,mean)
        conditional += pmf.get(spin,F(0))*cumulative
    return conditional
conditional = bonus_expectation()
base_ev = expected_spin(base,1)
scatter_ev = 5*trigger
bonus_ev = trigger*conditional
total = base_ev+scatter_ev+bonus_ev
target = F(state['targetRtpPercent'], 100)
low = base_ev+scatter_ev+trigger*bonus_expectation(F(2))
high = base_ev+scatter_ev+trigger*bonus_expectation(F(3))
p3 = (target-low)/(high-low)
weight3 = round(p3*state['calibrationWeightTotal'])
reference = {'scope':'Independent exact expectation, no full-round PMF claim', 'rtp':float(total), 'rationalRtp':str(total),
    'baseRtp':float(base_ev), 'rationalBaseRtp':str(base_ev), 'scatterRtp':float(scatter_ev), 'rationalScatterRtp':str(scatter_ev),
    'bonusRtp':float(bonus_ev), 'trigger':str(trigger), 'conditionalBonus':float(conditional),
    'targetRtp':float(target), 'rationalTargetRtp':str(target), 'rationalTargetDelta':str(total-target),
    'targetMet':abs(total-target) <= F(1,10**12), 'targetTolerance':'1/1000000000000',
    'calibration': {'rationalRtp2':str(low), 'rationalRtp3':str(high), 'rationalRequiredP3':str(p3),
        'weight2':state['calibrationWeightTotal']-weight3, 'weight3':weight3}}
parser = argparse.ArgumentParser()
parser.add_argument('--write',action='store_true');parser.add_argument('--check',action='store_true')
args = parser.parse_args()
golden = ROOT / 'backend/SlotMath.Core.Tests/TestData/DogHouse/dog-house-independent-expectation.json'
if args.check:
    assert json.loads(golden.read_text()) == reference, 'Independent Dog House expectation fixture is stale'
    assert reference['targetMet'], 'The default constructor graph does not meet its target RTP'
    print('Independent exact expectation and 98% target verified.')
elif args.write:
    golden.write_text(json.dumps(reference,indent=2)+'\n')
    print('Independent exact expectation fixture written.')
else:
    print(json.dumps(reference,indent=2))
