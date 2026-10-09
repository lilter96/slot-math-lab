#!/usr/bin/env python3
"""Independent, exact counterexamples for the measurement specification.

These are manual toy models, not tests of the production collector. They expose
questions that a collector/acceptance system must distinguish. No game engine
or graph compiler code is imported.
"""

import argparse
import json
import math
from fractions import Fraction as F


def moments(law):
    assert sum(law.values(), F(0)) == 1
    mean = sum((value * probability for value, probability in law.items()), F(0))
    second = sum((value * value * probability for value, probability in law.items()), F(0))
    return mean, second - mean * mean


def references():
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

    episodes = [[F(10)], [F(0)] * 9]
    pooled_mean = sum(map(sum, episodes), F(0)) / sum(map(len, episodes))
    episode_mean = sum(map(sum, episodes), F(0)) / len(episodes)
    mean_of_episode_means = sum((sum(e) / len(e) for e in episodes), F(0)) / len(episodes)
    assert pooled_mean == 1 and episode_mean == mean_of_episode_means == 5

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
        "sameRtpDifferentDistributions": {
            "rtp": str(mean_a),
            "varianceA": str(variance_a), "varianceB": str(variance_b),
            "hitProbabilityA": str(hit_a), "hitProbabilityB": str(hit_b),
        },
        "subjectWeighting": {
            "pooledFsMean": str(pooled_mean), "episodeTotalMean": str(episode_mean),
            "meanOfEpisodeMeans": str(mean_of_episode_means),
        },
        "capSemantics": {
            "capReached": str(reached), "rawCapExceeded": str(exceeded),
            "meanDeduction": str(deduction),
        },
        "covariance": {"sumComponentVariances": "2", "totalVariance": "4", "covariance": "1"},
        "pruning": {"knownMean": "1/2", "meanUpper": "5/2", "knownSecondMoment": "1", "secondMomentUpper": "17"},
        "markovFeature": {"expectedDuration": str(duration), "expectedReward": str(reward)},
        "rareEvents": {"zeroSuccessUpper95At100000": zero_upper, "trialsFor95ChanceOfOneEventAt1eMinus7": one_event_trials},
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="Check the exact reference examples and print their results.")
    parser.parse_args()
    print(json.dumps(references(), indent=2))
