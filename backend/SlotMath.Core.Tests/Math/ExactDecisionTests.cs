using SlotMath.Core.Math;

namespace SlotMath.Core.Tests.Math;

public sealed class ExactDecisionTests
{
    private static DecisionAction Pay(int id, Rational payout, Rational? wager = null) =>
        new(id, wager ?? Rational.Zero, new DecisionOutcome[] { new(-1, 1, payout) });

    private static ExactDecisionModel HiddenChoice(bool observationsRevealState = false)
    {
        DecisionState[] states =
        [
            new(0, true, 1, 1, new[] { Pay(7, new Rational(8, 5)) }),
            new(observationsRevealState ? 1 : 0, true, 1, 1, new[] { Pay(7, 0) })
        ];
        return ExactDecisionModel.Create(states, [new Rational(1, 2), new Rational(1, 2)], 1);
    }

    private static ExactDecisionModel InformationGame(bool signal, bool paidObservation = false)
    {
        DecisionState[] states = Enumerable.Range(0, 2).Select(state => new DecisionState(0, true, 1, 1,
            new[]
            {
                new(0, 0, new DecisionOutcome[]
                {
                    new(state, 1,
                        paidObservation && state == 1 ? new Rational(1, 10) : Rational.Zero, signal ? state : 0)
                }),
                Pay(1, state == 0 ? 2 : 0)
            })).ToArray();
        return ExactDecisionModel.Create(states, [new Rational(1, 2), new Rational(1, 2)], 1);
    }

    [Fact]
    public void NegativeFixedPlayRtpCanHideAnObservableConditionalExploit()
    {
        // Fixed play returns 0.8 for an entry cost of one; adaptive visible play returns 1.3.
        ExactDecisionModel hiddenModel = HiddenChoice();
        ExactDecisionResult hidden = ExactDecisionSolver.Solve(hiddenModel, 1);
        ExactDecisionResult revealed = ExactDecisionSolver.Solve(hiddenModel, 1, DecisionInformation.FullState);
        Assert.Equal(Rational.Zero, hidden.ExpectedNet);
        Assert.Equal(new Rational(3, 10), revealed.ExpectedNet);
        Assert.Equal(DecisionChoice.Stop, Assert.Single(hidden.Roots).Node.Choice);
        Assert.Equal(DecisionChoice.Action, revealed.Roots.Single(root => root.Observation == 0).Node.Choice);
        Assert.Equal(DecisionChoice.Stop, revealed.Roots.Single(root => root.Observation == 1).Node.Choice);
        Assert.Equal(revealed.ExpectedNet, ExactDecisionSolver.Solve(HiddenChoice(true), 1).ExpectedNet);
        Assert.Equal(hidden.ExpectedNet, DecisionPolicyOracle.Enumerate(hiddenModel, 1, hidden.Information).BestNet);
        Assert.Equal(revealed.ExpectedNet,
            DecisionPolicyOracle.Enumerate(hiddenModel, 1, revealed.Information).BestNet);
        Assert.Equal(revealed.ExpectedNet, DecisionPolicyOracle.Replay(revealed));
        ExactDecisionPotential failed = ExactDecisionPotential.Verify(hiddenModel, [1, 1]);
        Assert.False(failed.Holds);
        Assert.Null(failed.InitialNetUpperBound);
        Assert.Equal(DecisionPotentialViolationKind.ActionDrift, failed.FirstViolation!.Kind);
        Assert.Equal(new Rational(3, 5), failed.FirstViolation.Excess);
    }

    [Fact]
    public void InformationGatheringActionsImproveOnlyWhenTheirSignalsAreObserved()
    {
        ExactDecisionResult informed = ExactDecisionSolver.Solve(InformationGame(true), 2);
        ExactDecisionResult blind = ExactDecisionSolver.Solve(InformationGame(false), 2);
        Assert.Equal(new Rational(1, 2), informed.ExpectedNet);
        Assert.Equal(Rational.Zero, blind.ExpectedNet);
        Assert.Equal(0, Assert.Single(informed.Roots).Node.ActionId);
        Assert.Equal(2, informed.Roots[0].Node.Branches.Count);
        foreach (DecisionPolicyBranch branch in informed.Roots[0].Node.Branches)
        {
            Assert.Equal(new Rational(1, 2), branch.Probability);
            Assert.Equal(Rational.One, Assert.Single(branch.Next!.Belief).Probability);
        }

        Assert.Equal(informed.ExpectedNet, DecisionPolicyOracle.Replay(informed));
        (Rational BestNet, long Policies) oracle =
            DecisionPolicyOracle.Enumerate(informed.Model, 2, informed.Information);
        Assert.True(oracle.Policies > 1);
        Assert.Equal(informed.ExpectedNet, oracle.BestNet);
    }

    [Fact]
    public void RealizedPayoutsUpdateBeliefsEvenWithIdenticalAuthoredSignals()
    {
        ExactDecisionResult result = ExactDecisionSolver.Solve(InformationGame(false, true), 2);
        // High path: 0+2; low path: 1/10+1/10+1. Equal weights, entry cost one => net 3/5.
        Assert.Equal(new Rational(3, 5), result.ExpectedNet);
        Assert.Equal(2, Assert.Single(result.Roots).Node.Branches.Count);
        Assert.All(result.Roots[0].Node.Branches, branch => Assert.Equal(0, branch.Observation.Signal));
        Assert.Equal(0, result.Roots[0].Node.Branches.Single(branch =>
            branch.Observation.Payout == new Rational(1, 10)).Next!.ActionId);
        Assert.Equal(result.ExpectedNet, DecisionPolicyOracle.Enumerate(result.Model, 2, result.Information).BestNet);
        Assert.Equal(result.ExpectedNet, DecisionPolicyOracle.Replay(result));
    }

    [Fact]
    public void CostsDiscountAndForcedSettlementHavePinnedTimingAndNoDoublePayment()
    {
        DecisionState[] states =
        [
            new(0, false, 100, 5,
                new DecisionAction[] { new(9, 2, new DecisionOutcome[] { new(0, 1, 3) }) })
        ];
        var model = ExactDecisionModel.Create(states, [Rational.One], 1, new Rational(1, 2));
        Assert.Equal(new Rational(4, 1), ExactDecisionSolver.Solve(model, 0).ExpectedNet);
        Assert.Equal(Rational.One, ExactDecisionSolver.Solve(model, 1).ExpectedNet);
        ExactDecisionResult two = ExactDecisionSolver.Solve(model, 2);
        Assert.Equal(new Rational(-1, 2), two.ExpectedNet);
        Assert.Equal(two.ExpectedNet, DecisionPolicyOracle.Replay(two));
        var terminal = ExactDecisionModel.Create(
            [new DecisionState(0, false, 100, 5, new[] { Pay(9, 3, 2) })], [Rational.One], 1, new Rational(1, 2));
        Assert.Equal(new Rational(-3, 2), ExactDecisionSolver.Solve(terminal, 2).ExpectedNet);
    }

    [Fact]
    public void AuthoredBankrollStatesCanRemoveFurtherWagers()
    {
        var model = ExactDecisionModel.Create([
            new DecisionState(0, false, 0, 0,
                new DecisionAction[] { new(7, 1, new DecisionOutcome[] { new(1, 1, 0) }) }),
            new DecisionState(1, true, 0, 0, Array.Empty<DecisionAction>())
        ], [Rational.One, Rational.Zero], 0);
        ExactDecisionResult result = ExactDecisionSolver.Solve(model, 5);
        Assert.Equal(new Rational(-1, 1), result.ExpectedNet);
        Assert.Equal(DecisionChoice.Stop, result.Roots[0].Node.Branches[0].Next!.Choice);
        Assert.Equal(result.ExpectedNet, DecisionPolicyOracle.Replay(result));
    }

    [Fact]
    public void AllActionPotentialProvesABoundedHorizonHouseEdge()
    {
        Rational[] cashout = [new(47, 50), new(47, 25), new(94, 25)];
        DecisionState[] states = Enumerable.Range(0, 3).Select(state => new DecisionState(state, true,
            cashout[state], cashout[state],
            state == 2
                ? []
                : new DecisionAction[]
                {
                    new(1, 0,
                        new DecisionOutcome[]
                        {
                            new(state + 1, new Rational(9, 20), 0), new(-1, new Rational(11, 20), 0)
                        })
                })).ToArray();
        var model = ExactDecisionModel.Create(states, [Rational.One, Rational.Zero, Rational.Zero], 1);
        ExactDecisionPotential proof = ExactDecisionPotential.Verify(model, cashout);
        Assert.True(proof.Holds);
        Assert.True(proof.ProvesNoPositiveExpectedNet);
        Assert.Equal(new Rational(-3, 50), proof.InitialNetUpperBound);
        for (var horizon = 0; horizon <= 4; horizon++)
        {
            ExactDecisionResult result = ExactDecisionSolver.Solve(model, horizon);
            Assert.Equal(new Rational(-3, 50), result.ExpectedNet);
            Assert.True(result.ExpectedNet <= proof.InitialNetUpperBound);
        }

        (Rational BestNet, long Policies) oracle =
            DecisionPolicyOracle.Enumerate(model, 2, DecisionInformation.FullState);
        Assert.Equal(new Rational(-3, 50), oracle.BestNet);
        cashout[0] = 100;
        Assert.Equal(new Rational(47, 50), proof.Potential[0]);
    }

    [Fact]
    public void AFailedGlobalPotentialIsNotAutomaticallyAnExploitableInitialPolicy()
    {
        var model = ExactDecisionModel.Create([
            new DecisionState(0, true, 0, 0, new[] { Pay(0, 0) }),
            new DecisionState(1, true, 0, 0, new[] { Pay(0, 3) })
        ], [Rational.One, Rational.Zero], 0);
        ExactDecisionPotential proof = ExactDecisionPotential.Verify(model, [0, 0]);
        Assert.False(proof.Holds);
        Assert.Equal(1, proof.FirstViolation!.State);
        Assert.Equal(Rational.Zero, ExactDecisionSolver.Solve(model, 2).ExpectedNet);
        Assert.Null(proof.InitialNetUpperBound);
        var settlements = ExactDecisionModel.Create(
            [new DecisionState(0, true, 2, 3, Array.Empty<DecisionAction>())], [Rational.One], 0);
        ExactDecisionPotential missing = ExactDecisionPotential.Verify(settlements, [1]);
        Assert.Equal(2, missing.ViolationCount);
        Assert.Equal(DecisionPotentialViolationKind.HorizonSettlement, missing.FirstViolation!.Kind);
    }

    [Fact]
    public void TiesPreferStoppingThenTheSmallestActionId()
    {
        var stop = new DecisionState(0, true, 1, 1, new[] { Pay(9, 1), Pay(2, 1) });
        var model = ExactDecisionModel.Create([stop], [Rational.One], 0);
        Assert.Equal(DecisionChoice.Stop, ExactDecisionSolver.Solve(model, 1).Roots[0].Node.Choice);
        model = ExactDecisionModel.Create([
            stop with
            {
                CanStop = false
            }
        ], [Rational.One], 0);
        Assert.Equal(2, ExactDecisionSolver.Solve(model, 1).Roots[0].Node.ActionId);
        model = ExactDecisionModel.Create([
            new DecisionState(0, false, 0, 0, new[] { Pay(9, 0), Pay(2, 2) }),
            new DecisionState(0, false, 0, 0, new[] { Pay(2, 0), Pay(9, 4) })
        ], [new Rational(1, 2), new Rational(1, 2)], 0);
        ExactDecisionResult reordered = ExactDecisionSolver.Solve(model, 1);
        Assert.Equal(new Rational(2, 1), reordered.ExpectedNet);
        Assert.Equal(9, reordered.Roots[0].Node.ActionId);
    }

    [Fact]
    public void PinnedPoliciesAgreeWithExhaustiveHistoryStrategiesAndForwardReplay()
    {
        var random = new System.Random(6109);
        for (var sample = 0; sample < 16; sample++)
        {
            var states = new DecisionState[2];
            for (var state = 0; state < 2; state++)
            {
                var actions = new DecisionAction[2];
                for (var action = 0; action < 2; action++)
                {
                    DecisionOutcome[] outcomes = Enumerable.Range(0, 2).Select(outcome => new DecisionOutcome(
                        random.Next(-1, 2), new Rational(outcome + 1, 3), random.Next(0, 3))).ToArray();
                    actions[action] = new DecisionAction(action, new Rational(action, 10), outcomes);
                }

                states[state] = new DecisionState(0, true, random.Next(0, 3), random.Next(0, 3), actions);
            }

            var model = ExactDecisionModel.Create(states, [new Rational(1, 2), new Rational(1, 2)], 1,
                sample % 2 == 0 ? Rational.One : new Rational(3, 4));
            ExactDecisionResult hidden = ExactDecisionSolver.Solve(model, 2);
            ExactDecisionResult full = ExactDecisionSolver.Solve(model, 2, DecisionInformation.FullState);
            Assert.Equal(DecisionPolicyOracle.Enumerate(model, 2, hidden.Information).BestNet, hidden.ExpectedNet);
            Assert.Equal(hidden.ExpectedNet, DecisionPolicyOracle.Replay(hidden));
            Assert.Equal(full.ExpectedNet, DecisionPolicyOracle.Replay(full));
            Assert.True(full.ExpectedNet >= hidden.ExpectedNet);
            Assert.Equal(DecisionPolicyOracle.Enumerate(model, 2, full.Information).BestNet, full.ExpectedNet);
        }
    }

    [Fact]
    public void ModelAndPolicyEvidenceOwnTheirCollections()
    {
        DecisionOutcome[] outcomes = [new(-1, 1, 2)];
        DecisionAction[] actions = [new(7, 0, outcomes)];
        DecisionState[] states = [new(0, false, 0, 0, actions)];
        Rational[] initial = [Rational.One];
        var model = ExactDecisionModel.Create(states, initial, 1);
        outcomes[0] = new DecisionOutcome(-1, 1, 999);
        actions[0] = Pay(7, 999);
        states[0] = states[0] with
        {
            HorizonPayout = 999
        };
        initial[0] = Rational.Zero;
        ExactDecisionResult result = ExactDecisionSolver.Solve(model, 1);
        Assert.Equal(Rational.One, result.ExpectedNet);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<DecisionOutcome>)model.States[0].Actions[0].Outcomes)[0] = default);
        Assert.Throws<NotSupportedException>(() => ((IList<DecisionState>)model.States)[0] = states[0]);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<DecisionBeliefAtom>)result.Roots[0].Node.Belief)[0] = default);
    }

    [Fact]
    public void DeepHorizonsUseIterativeBeliefGraphPasses()
    {
        var model = ExactDecisionModel.Create([
            new DecisionState(0, false, 0, 1,
                new DecisionAction[] { new(0, 0, new DecisionOutcome[] { new(0, 1, 0) }) })
        ], [Rational.One], 0);
        ExactDecisionResult result = ExactDecisionSolver.Solve(model, 4096,
            options: new ExactDecisionOptions
            {
                MaxHorizon = 4096
            });
        Assert.Equal(Rational.One, result.ExpectedNet);
        Assert.Equal(4097, result.EvaluatedBeliefs);
        DecisionPolicyNode node = result.Roots[0].Node;
        for (var remaining = 4096; remaining > 0; remaining--)
        {
            Assert.Equal(remaining, node.Remaining);
            node = Assert.Single(node.Branches).Next!;
        }

        Assert.Equal(DecisionChoice.Horizon, node.Choice);
    }

    [Fact]
    public void InvalidInformationModelsCannotProducePolicies()
    {
        DecisionState valid = new(0, true, 0, 0, new[] { Pay(0, 1) });

        void Reject(DecisionState state)
        {
            Assert.Throws<ArgumentException>(() =>
                ExactDecisionModel.Create([state], [Rational.One], 0));
        }

        Reject(valid with
        {
            Actions = new[] { Pay(0, -1) }
        });
        Reject(valid with
        {
            Actions = new[] { Pay(0, 1, -1) }
        });
        Reject(valid with
        {
            Actions = new[] { Pay(0, 1), Pay(0, 2) }
        });
        Reject(valid with
        {
            CanStop = false,
            Actions = Array.Empty<DecisionAction>()
        });
        Reject(valid with
        {
            Actions = new DecisionAction[] { new(0, 0, new DecisionOutcome[] { new(-1, new Rational(1, 2), 1) }) }
        });
        Reject(valid with
        {
            Actions = new DecisionAction[] { new(0, 0, new DecisionOutcome[] { new(2, 1, 1) }) }
        });
        Reject(valid with
        {
            Actions = new DecisionAction[] { new(0, 0, new DecisionOutcome[] { new(-1, default, 1) }) }
        });
        Assert.Throws<ArgumentException>(() => ExactDecisionModel.Create([valid], [new Rational(1, 2)], 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExactDecisionModel.Create([valid], [Rational.One], 0, 0));
        Assert.Throws<ArgumentException>(() => ExactDecisionModel.Create([
                valid, valid with
                {
                    CanStop = false
                }
            ],
            [new Rational(1, 2), new Rational(1, 2)], 0));
        Assert.Throws<ArgumentException>(() => ExactDecisionModel.Create([
            valid, valid with
            {
                Actions = new[] { Pay(0, 1, 1) }
            }
        ], [new Rational(1, 2), new Rational(1, 2)], 0));
    }

    [Fact]
    public void RepresentationArithmeticAndCancellationLimitsReturnNoPartialProof()
    {
        ExactDecisionModel model = InformationGame(true);
        Assert.Throws<ExactLinearLimitException>(() => ExactDecisionSolver.Solve(model, 2,
            options: new ExactDecisionOptions
            {
                MaxPolicyNodes = 1
            }));
        Assert.Throws<ExactLinearLimitException>(() => ExactDecisionSolver.Solve(model, 2,
            options: new ExactDecisionOptions
            {
                MaxPolicyBranches = 1
            }));
        Assert.Throws<ExactLinearLimitException>(() => ExactDecisionSolver.Solve(model, 2,
            options: new ExactDecisionOptions
            {
                MaxBeliefEntries = 1
            }));
        Assert.Throws<ExactLinearLimitException>(() => ExactDecisionSolver.Solve(model, 1,
            options: new ExactDecisionOptions
            {
                MaxHorizon = 0
            }));
        Assert.Throws<ExactLinearLimitException>(() => ExactDecisionSolver.Solve(model, 1,
            options: new ExactDecisionOptions
            {
                MaxWork = 1
            }));
        Assert.Throws<ExactLinearLimitException>(() => ExactDecisionPotential.Verify(model, [1, 1],
            new ExactDecisionOptions
            {
                MaxStates = 1
            }));
        Assert.Throws<ArgumentException>(() => ExactDecisionPotential.Verify(model, [1]));
        Assert.Throws<ArgumentException>(() => ExactDecisionPotential.Verify(model, [default, 1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExactDecisionSolver.Solve(model, 1, (DecisionInformation)99));
        var canceled = new CancellationToken(true);
        Assert.Throws<OperationCanceledException>(() => ExactDecisionSolver.Solve(model, 1, cancellation: canceled));
        Assert.Throws<OperationCanceledException>(() =>
            ExactDecisionPotential.Verify(model, [1, 1], cancellation: canceled));
        Assert.Throws<OperationCanceledException>(() =>
            ExactDecisionModel.Create(model.States, model.Initial, 1, cancellation: canceled));
        var growth = ExactDecisionModel.Create([
            new DecisionState(0, false, 0, 1,
                new DecisionAction[] { new(0, 0, new DecisionOutcome[] { new(0, 1, 0) }) })
        ], [Rational.One], 0, new Rational(1, 2));
        Assert.Throws<ExactLinearLimitException>(() => ExactDecisionSolver.Solve(growth, 8,
            options: new ExactDecisionOptions
            {
                MaximumBits = 4
            }));
    }
}
