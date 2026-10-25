namespace SlotMath.Core.Compiler.Ir;

internal static class IrVerifier
{
    internal static IReadOnlyList<string> Verify(ExpressionIr program)
    {
        var errors = new List<string>();
        var definitions = new HashSet<int>();
        var operations = new HashSet<int>();
        var regions = new HashSet<int>();
        Visit(program.Root, []);
        return errors.AsReadOnly();

        static bool Same(IrEffects a, IrEffects b)
        {
            return a.Flags == b.Flags && a.Reads.SetEquals(b.Reads)
                                      && a.Writes.SetEquals(b.Writes) &&
                                      a.Bindings.SetEquals(b.Bindings);
        }

        void Define(IrValue value)
        {
            if (value.Id <= 0)
            {
                errors.Add("SSA value IDs must be positive.");
            }

            if (!definitions.Add(value.Id))
            {
                errors.Add($"Value {value.Id} has multiple definitions.");
            }
        }

        void Visit(IrRegion region, HashSet<int> inherited)
        {
            if (!Same(region.Effects, IrEffects.Union(region.Operations.Select(operation => operation.Effects))))
            {
                errors.Add($"Region {region.Id} has an inconsistent effect summary.");
            }

            if (!regions.Add(region.Id))
            {
                errors.Add($"Region {region.Id} is reused.");
            }

            var available = new HashSet<int>(inherited);
            foreach (IrValue parameter in region.Parameters)
            {
                Define(parameter);
                available.Add(parameter.Id);
            }

            foreach (IrOperation operation in region.Operations)
            {
                IrEffects expected =
                    IrEffects.Union(
                        new[] { operation.OwnEffects }.Concat(operation.Regions.Select(child => child.Effects)));
                if (!Same(operation.Effects, expected))
                {
                    errors.Add($"Operation {operation.Id} has an inconsistent effect summary.");
                }

                if (!operations.Add(operation.Id))
                {
                    errors.Add($"Operation {operation.Id} is reused.");
                }

                foreach (IrValue value in operation.Inputs)
                {
                    if (!available.Contains(value.Id))
                    {
                        errors.Add($"Value {value.Id} does not dominate operation {operation.Id}.");
                    }
                }

                foreach (IrRegion child in operation.Regions)
                {
                    Visit(child, available);
                }

                Define(operation.Result);
                available.Add(operation.Result.Id);
            }

            if (!available.Contains(region.Result.Id))
            {
                errors.Add($"Value {region.Result.Id} does not dominate region {region.Id}'s result.");
            }
        }
    }
}
