namespace SlotMath.Core.Compiler.Ir;

internal static class MemorySsaVerifier
{
    internal static IReadOnlyList<string> Verify(MemorySsa memory)
    {
        var errors = new List<string>();
        var regions = new HashSet<int>();
        var steps = new HashSet<int>();
        var loopHeaders = new HashSet<int>();
        foreach (MemoryFunction function in memory.Functions.Values)
        {
            Visit(function.Body);
        }

        Visit(memory.Root);
        foreach ((var id, MemoryVersion version) in memory.Versions)
        {
            if (id <= 0 || id != version.Id)
            {
                errors.Add("Invalid memory version identity.");
            }

            if (version.Kind == MemoryKind.Input && version.Inputs.Count != 0)
            {
                errors.Add($"Input {id} has predecessors.");
            }

            if (version.Kind is MemoryKind.Write or MemoryKind.Call && version.Inputs.Count != 1)
            {
                errors.Add($"Definition {id} requires one predecessor.");
            }

            if (version.Kind == MemoryKind.Phi && version.Inputs.Count < 2)
            {
                errors.Add($"Phi {id} requires merge inputs.");
            }

            foreach (var input in version.Inputs)
            {
                if (!memory.Versions.ContainsKey(input))
                {
                    errors.Add($"Memory {id} references missing predecessor {input}.");
                }

                if (input >= id && !loopHeaders.Contains(id))
                {
                    errors.Add($"Memory {id} has an unstructured cycle/forward input.");
                }
            }
        }

        return errors.AsReadOnly();

        void Visit(MemoryRegion region)
        {
            if (!regions.Add(region.Id))
            {
                errors.Add($"Region {region.Id} is reused.");
                return;
            }

            if (!memory.Versions.ContainsKey(region.Input))
            {
                errors.Add($"Region {region.Id} has a missing input.");
            }

            var current = region.Input;
            foreach (MemoryStep step in region.Steps)
            {
                if (!steps.Add(step.Id))
                {
                    errors.Add($"Step {step.Id} is reused.");
                }

                if (step.Input != current)
                {
                    errors.Add($"Step {step.Id} does not consume the current memory version.");
                }

                if (!memory.Versions.TryGetValue(step.Result, out MemoryVersion? output))
                {
                    errors.Add($"Step {step.Id} has a missing result.");
                    continue;
                }

                if (step.Kind == MemoryStepKind.Write &&
                    (output.Kind != MemoryKind.Write || output.Inputs.Count != 1 || output.Inputs[0] != current
                     || !output.Writes.SetEquals(step.Effects.Writes)))
                {
                    errors.Add($"Step {step.Id} has an invalid write definition.");
                }

                if (step.Call is { } call)
                {
                    if (call.Input != step.Input || call.Result != step.Result ||
                        !memory.Functions.TryGetValue(call.Target, out MemoryFunction? callee))
                    {
                        errors.Add($"Step {step.Id} has an invalid call link.");
                    }
                    else if (!callee.Effects.Writes.IsSubsetOf(step.Effects.Writes) ||
                             (!callee.Effects.Known && !step.Effects.Flags.HasFlag(IrEffect.Unknown)))
                    {
                        errors.Add($"Step {step.Id} understates callee effects.");
                    }

                    if (step.Result != step.Input && (output.Kind != MemoryKind.Call || output.Inputs.Count != 1 ||
                                                      output.Inputs[0] != current ||
                                                      !output.MayWrite))
                    {
                        errors.Add($"Step {step.Id} has an invalid call definition.");
                    }
                }

                if (step.Kind == MemoryStepKind.Loop)
                {
                    if (step.LoopHeader is not { } header || !memory.Versions.TryGetValue(header,
                                                              out MemoryVersion? phi)
                                                          || phi.Kind != MemoryKind.Phi || phi.Inputs.Count != 2 ||
                                                          step.Regions.Count != 3)
                    {
                        errors.Add($"Loop {step.Id} has no entry/backedge phi.");
                    }
                    else
                    {
                        loopHeaders.Add(header);
                        MemoryRegion guard = step.Regions[0];
                        MemoryRegion body = step.Regions[1];
                        MemoryRegion exit = step.Regions[2];
                        if (phi.Inputs[0] != step.Input || phi.Inputs[1] != body.Result || guard.Input != header
                            || body.Input != guard.Result || exit.Input != guard.Result || step.Result != exit.Result)
                        {
                            errors.Add($"Loop {step.Id} has inconsistent entry/backedge/exit memory.");
                        }
                    }
                }
                else if (step.Kind is MemoryStepKind.Branch or MemoryStepKind.Observe)
                {
                    if (step.Regions.Any(child => child.Input != current))
                    {
                        errors.Add($"Step {step.Id} forks from a wrong memory version.");
                    }

                    HashSet<int> inputs = step.Regions.Select(child => child.Result).ToHashSet();
                    if (step.Regions.Count == 1)
                    {
                        inputs.Add(step.Input);
                    }

                    if (step.Result != step.Input &&
                        (output.Kind != MemoryKind.Phi || !inputs.SetEquals(output.Inputs)))
                    {
                        errors.Add($"Step {step.Id} has an invalid conditional merge.");
                    }

                    if (step.Result == step.Input && inputs.Any(input => input != current))
                    {
                        errors.Add($"Step {step.Id} discards conditional writes.");
                    }
                }

                foreach (MemoryRegion child in step.Regions)
                {
                    Visit(child);
                }

                current = step.Result;
            }

            if (current != region.Result)
            {
                errors.Add($"Region {region.Id} returns stale memory.");
            }

            IrEffects effects = IrEffects.Union(region.Steps.Select(step => step.Effects));
            if (effects.Flags != region.Effects.Flags || !effects.Reads.SetEquals(region.Effects.Reads)
                                                      || !effects.Writes.SetEquals(region.Effects.Writes) ||
                                                      !effects.Bindings.SetEquals(region.Effects.Bindings))
            {
                errors.Add($"Region {region.Id} has inconsistent effects.");
            }
        }
    }
}
