#!/usr/bin/env python3
"""Independent, exact counterexamples for the measurement specification.

These are manual toy models, not tests of the production collector. They expose
questions that a collector/acceptance system must distinguish. No game engine
or graph compiler code is imported.
"""

import argparse
import json
import math
from itertools import combinations
from fractions import Fraction as F


def moments(law):
    assert sum(law.values(), F(0)) == 1
    mean = sum((value * probability for value, probability in law.items()), F(0))
    second = sum((value * value * probability for value, probability in law.items()), F(0))
    return mean, second - mean * mean


def references():
    # Independent identity-set oracle: repeated children activate an owner
    # once, accepted zeros still match, and nested ancestors receive no child.
    # The B episode is exit-excluded; that does not remove accepted exposure.
    children = [(0, 'A', 'sticky', 0, True, True), (0, 'A', 'sticky', 1, True, True),
                (0, 'B', 'sticky', 2, True, True), (1, 'inner', 'other', 0, True, True),
                (2, 'C', 'sticky', 5, False, True), (3, 'discarded', 'other', 6, True, False)]
    accepted = [c for c in children if c[4] and c[5]]
    matching_rounds = {c[0] for c in accepted}
    matching_episodes = {(c[0], c[1]) for c in accepted}
    assert len(accepted) == 4 and len(matching_rounds) == 2 and len(matching_episodes) == 3
    assert len({c[0] for c in accepted if c[2] == 'sticky'}) == 1
    assert len({(c[0], c[1]) for c in accepted if c[2] == 'sticky'}) == 2
    assert not any(c[1] == 'outer' for c in accepted)
    # Constructor AST fixture checked independently with Python's rational
    # arithmetic, including negative floor and a fractional indexed value.
    fractions = [F(-6, 12), F(8, 12), F(15, 12)]
    fractional_payout = sum(fractions) * 12 + min(F(3, 2), F(7, 4)) * 4 + abs(fractions[0]) * 2 + math.floor(F(-1, 2)) + 1
    assert fractional_payout == 24 and abs(fractions[0]) == F(1, 2)
    model_a = {F(0): F(1, 2), F(49, 25): F(1, 2)}
    model_b = {F(0): F(9, 10), F(49, 5): F(1, 10)}
    mean_a, variance_a = moments(model_a)
    mean_b, variance_b = moments(model_b)
    assert mean_a == mean_b == F(49, 50)
    assert variance_a == F(2401, 2500) and variance_b == F(21609, 2500)
    hit_a = sum(p for x, p in model_a.items() if x > 0)
    hit_b = sum(p for x, p in model_b.items() if x > 0)
    assert hit_a == F(1, 2) and hit_b == F(1, 10)
    assert sum(p for x, p in model_a.items() if x >= 5) == 0
    assert sum(p for x, p in model_b.items() if x >= 5) == F(1, 10)
    support = sorted(set(model_a) | set(model_b))
    # Definition-based exhaustive event oracle, independent of the production
    # half-L1 calculation: TV is the largest discrepancy over every event.
    total_variation = max(abs(sum((model_a.get(x, F(0)) - model_b.get(x, F(0)) for x in event), F(0)))
                          for size in range(len(support) + 1) for event in combinations(support, size))
    cdf_distance = max(abs(sum((p for x, p in model_a.items() if x <= edge), F(0))
                           - sum((p for x, p in model_b.items() if x <= edge), F(0))) for edge in support)
    assert total_variation == F(1, 2) and cdf_distance == F(2, 5)

    episodes = [[F(10)], [F(0)] * 9]
    pooled_mean = sum(map(sum, episodes), F(0)) / sum(map(len, episodes))
    episode_mean = sum(map(sum, episodes), F(0)) / len(episodes)
    mean_of_episode_means = sum((sum(e) / len(e) for e in episodes), F(0)) / len(episodes)
    assert pooled_mean == 1 and episode_mean == mean_of_episode_means == 5

    # Two externally paid rounds; only the first contains two FS reveals.
    # Absent cohorts retain the second parent's external cost in contribution.
    external_costs, matching_reveals = [F(2), F(2)], [F(1), F(3)]
    contribution = sum(matching_reveals) / sum(external_costs)
    assert contribution == 1 and sum(matching_reveals) / len(matching_reveals) == 2

    # Short decimal money is exact. The hypothetical fixed-horizon trajectory
    # records ruin at the first point that cannot fund the next 0.1 wager.
    bankroll, wager = F('0.3'), F('0.1')
    first_ruin = None
    for round_number in range(1, 4):
        bankroll -= wager
        if first_ruin is None and bankroll < wager:
            first_ruin = round_number
    assert first_ruin == 3 and bankroll == 0
    cent_bank, peak, drawdown = 30, 30, 0
    balances = []
    for payout in [0, 25, 5, 0, 10]:
        cent_bank += payout - 10
        peak = max(peak, cent_bank)
        drawdown = max(drawdown, peak - cent_bank)
        balances.append(cent_bank)
    assert balances == [20, 35, 30, 20, 20] and drawdown == 15

    raw, cap = [F(0), F(100), F(120)], F(100)
    settled = [min(value, cap) for value in raw]
    reached = F(sum(value == cap for value in settled), len(settled))
    exceeded = F(sum(value > cap for value in raw), len(raw))
    deduction = sum((before - after for before, after in zip(raw, settled)), F(0)) / len(raw)
    assert reached == F(2, 3) and exceeded == F(1, 3) and deduction == F(20, 3)

    # Paired components are both zero or both two, each with probability 1/2.
    component_variance = moments({F(0): F(1, 2), F(2): F(1, 2)})[1]
    total_variance = moments({F(0): F(1, 2), F(4): F(1, 2)})[1]
    covariance = F(1)
    assert component_variance == 1 and total_variance == 4
    assert total_variance == 2 * component_variance + 2 * covariance

    # Independent sample ledger, rather than the production accumulator:
    # X=(0,2), Y=(0,2), Z=(2,0), T=(2,4). Include every cross term.
    vectors = [[F(0), F(2)], [F(0), F(2)], [F(2), F(0)]]
    means = [sum(v) / len(v) for v in vectors]
    sample_var = lambda v: sum((x - sum(v) / len(v)) ** 2 for x in v) / (len(v) - 1)
    matrix = [[sum((x - means[i]) * (y - means[j]) for x, y in zip(a, b)) / (len(a) - 1)
               for j, b in enumerate(vectors)] for i, a in enumerate(vectors)]
    diagonal = sum(matrix[i][i] for i in range(3))
    cross = 2 * sum(matrix[i][j] for i in range(3) for j in range(i + 1, 3))
    total = [sum(row) for row in zip(*vectors)]
    assert matrix == [[2, 2, -2], [2, 2, -2], [-2, -2, 2]]
    assert diagonal == 6 and cross == -4 and sample_var(total) == diagonal + cross == 2
    assert all(t == sum(row) for t, row in zip(total, zip(*vectors)))
    # Means of two equal-length samples with a constant total can conceal
    # per-observation mistakes; the exact residual must still count both.
    wrong_total = [F(3), F(3)]
    assert sum(wrong_total) == sum(total) and sum(t != sum(row) for t, row in zip(wrong_total, zip(*vectors))) == 2

    # Known mass: P(0)=1/2, P(2)=1/4. The last quarter is unresolved in [0,8].
    known_first, known_second, missing, bound = F(1, 2), F(1), F(1, 4), F(8)
    mean_upper = known_first + missing * bound
    second_upper = known_second + missing * bound * bound
    assert mean_upper == F(5, 2) and second_upper == 17
    for unresolved_value in range(9):
        full_mean = known_first + missing * unresolved_value
        full_second = known_second + missing * unresolved_value ** 2
        assert known_first <= full_mean <= mean_upper
        assert known_second <= full_second <= second_upper

    # A two-state transient feature: 0 -> 0/1 equally, 1 -> terminal surely.
    # N=(I-Q)^-1=[[2,1],[0,1]], so initial state 0 has duration 3.
    # Rewards are 1 at state 0 and 4 at state 1: expected total 6.
    expected_visits, rewards = [F(2), F(1)], [F(1), F(4)]
    duration = sum(expected_visits)
    reward = sum((visits * value for visits, value in zip(expected_visits, rewards)), F(0))
    assert duration == 3 and reward == 6
    assert duration == 1 + F(1, 2) * duration + F(1, 2) * 1
    assert reward == 1 + F(1, 2) * reward + F(1, 2) * 4

    zero_upper = -math.expm1(math.log(.05) / 100000)
    one_event_trials = math.ceil(math.log(.05) / math.log1p(-1e-7))
    assert 0 < zero_upper < .00003
    assert one_event_trials == 29957322

    return {
        "matchingParents": {"acceptedChildren": len(accepted), "paidRounds": len(matching_rounds),
                            "owningEpisodes": len(matching_episodes), "stickyPaidRounds": 1, "stickyEpisodes": 2},
        "fractionalConstructor": {"payout": str(fractional_payout), "trackedMagnitude": str(abs(fractions[0]))},
        "sameRtpDifferentDistributions": {
            "rtp": str(mean_a),
            "varianceA": str(variance_a), "varianceB": str(variance_b),
            "hitProbabilityA": str(hit_a), "hitProbabilityB": str(hit_b),
            "totalVariation": str(total_variation), "cdfDistance": str(cdf_distance),
        },
        "subjectWeighting": {
            "pooledFsMean": str(pooled_mean), "episodeTotalMean": str(episode_mean),
            "meanOfEpisodeMeans": str(mean_of_episode_means),
        },
        "paidTurnover": {"paidRounds": 2, "externalTurnover": "4", "revealMean": "2", "contribution": str(contribution)},
        "decimalSession": {"firstRuin": first_ruin, "endingBankroll": str(bankroll), "centBalances": balances, "centDrawdown": drawdown},
        "capSemantics": {
            "capReached": str(reached), "rawCapExceeded": str(exceeded),
            "meanDeduction": str(deduction),
        },
        "covariance": {"sumComponentVariances": "2", "totalVariance": "4", "covariance": "1"},
        "multiComponentSample": {"matrix": [[str(v) for v in row] for row in matrix], "mean": "3", "diagonal": "6", "cross": "-4", "totalVariance": "2", "equalMeanExactMismatches": 2},
        "pruning": {"knownMean": "1/2", "meanUpper": "5/2", "knownSecondMoment": "1", "secondMomentUpper": "17"},
        "markovFeature": {"expectedDuration": str(duration), "expectedReward": str(reward)},
        "rareEvents": {"zeroSuccessUpper95At100000": zero_upper, "trialsFor95ChanceOfOneEventAt1eMinus7": one_event_trials},
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="Check the exact reference examples and print their results.")
    parser.parse_args()
    print(json.dumps(references(), indent=2))
