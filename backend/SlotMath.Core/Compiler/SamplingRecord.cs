using System.Collections;
using System.Collections.ObjectModel;
using SlotMath.Core.Expressions;

namespace SlotMath.Core.Compiler;

// A record rental is immutable for the remainder of its round, just like an
// arena array. Export copies it; persistent runners disable the arena.
internal sealed class SamplingRecord : IReadOnlyDictionary<string, ExprValue>
{
    internal readonly ExprValue[] Fields;
    private readonly Shape _shape;
    private readonly ReadOnlyCollection<ExprValue> _values;

    internal SamplingRecord(Shape shape)
    {
        _shape = shape;
        Fields = new ExprValue[shape.Names.Count];
        _values = Array.AsReadOnly(Fields);
    }

    public int Count => Fields.Length;
    public IEnumerable<string> Keys => _shape.Names;
    public IEnumerable<ExprValue> Values => _values;
    public ExprValue this[string key] => Fields[_shape.Indices[key]];

    public bool ContainsKey(string key) => _shape.Indices.ContainsKey(key);

    public bool TryGetValue(string key, out ExprValue value)
    {
        if (_shape.Indices.TryGetValue(key, out var index))
        {
            value = Fields[index];
            return true;
        }

        value = default;
        return false;
    }

    public IEnumerator<KeyValuePair<string, ExprValue>> GetEnumerator()
    {
        for (var index = 0; index < Fields.Length; index++)
        {
            yield return new KeyValuePair<string, ExprValue>(_shape.Names[index], Fields[index]);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal ExprValue Finish()
    {
        var symbols = false;
        foreach (ref readonly ExprValue field in Fields.AsSpan())
        {
            symbols |= field.ContainsSymbols;
        }

        return ExprValue.RecordOwned(this, symbols);
    }

    internal sealed class Shape
    {
        internal readonly Dictionary<string, int> Indices;
        internal readonly ReadOnlyCollection<string> Names;

        internal Shape(string[] names)
        {
            Names = Array.AsReadOnly((string[])names.Clone());
            Indices = names.Select((name, index) => (name, index))
                .ToDictionary(pair => pair.name, pair => pair.index, StringComparer.Ordinal);
        }
    }
}
