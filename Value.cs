using System.Diagnostics;

namespace Reocla;

public sealed record LineInfo(string Filename, uint Line, uint Column)
{
    public string Prefix => $"{this}: ";
    public sealed override string ToString() => $"{Filename}:{Line}:{Column}";
}

public abstract record Value(LineInfo LineInfo) : IComparable<Value>
{
    public record Number(long Value, LineInfo Info) : Value(Info);
    public record Char(char Value, LineInfo Info) : Value(Info);
    public record Bool(bool Value, LineInfo Info) : Value(Info);
    public record Symbol(string Name, bool IsQuoted, LineInfo Info) : Value(Info);
    public record String(string Value, LineInfo Info) : Value(Info);
    public record List(List<Value> Items, LineInfo Info) : Value(Info);
    public record Tuple(List<Value> Items, bool IsQuoted, LineInfo Info) : Value(Info);

    public int CompareTo(Value? other)
    {
        if (other is null || GetType() != other.GetType())
        {
            return 1;
        }

        return this switch
        {
            Number i => i.Value.CompareTo(((Number)other).Value),
            Char c => c.Value.CompareTo(((Char)other).Value),
            Bool b => b.Value.CompareTo(((Bool)other).Value),
            Symbol s => string.Compare(s.Name, ((Symbol)other).Name, StringComparison.Ordinal),
            String s => string.Compare(s.Value, ((String)other).Value, StringComparison.Ordinal),
            List l => CompareLists(l.Items, ((List)other).Items),
            Tuple t => CompareLists(t.Items, ((Tuple)other).Items),
            _ => throw new UnreachableException($"Unhandled type: {GetType().FullName}"),
        };
    }

    private static int CompareLists(List<Value> a, List<Value> b)
    {
        int len = Math.Min(a.Count, b.Count);
        for (int i = 0; i < len; i++)
        {
            int cmp = a[i].CompareTo(b[i]);
            if (cmp != 0) return cmp;
        }
        return a.Count.CompareTo(b.Count);
    }

    public sealed override string ToString() => this switch
    {
        Number i => i.Value.ToString(),
        Char c => $"'{c.Value}'",
        Bool b => b.Value ? "#t" : "#f",
        Symbol sym => $"{(sym.IsQuoted ? "'" : "")}{sym.Name}",
        String str => $"\"{str.Value}\"",
        List list => $"[{string.Join(", ", list.Items)}]",
        Tuple tuple => $"{(tuple.IsQuoted ? "'" : "")}({string.Join(", ", tuple.Items)})",
        _ => throw new UnreachableException($"Unhandled type: {GetType().FullName}"),
    };
}
