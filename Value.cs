using System.Diagnostics;

namespace Reocla;

public sealed record LineInfo(string Filename, uint Line, uint Column)
{
    public string Prefix => $"{this}: ";
    public sealed override string ToString() => $"{Filename}:{Line}:{Column}";
}

public abstract record Value(LineInfo LineInfo)
{
    public sealed record Number(long Value, LineInfo Info)
        : Value(Info)
    {
        public sealed override string ToString() => Value.ToString();
    }

    public sealed record Char(char Value, LineInfo Info)
        : Value(Info)
    {
        public sealed override string ToString() => $"'{Value}'";
    }

    public sealed record Bool(bool Value, LineInfo Info)
        : Value(Info)
    {
        public sealed override string ToString() => Value ? "#t" : "#f";
    }

    public sealed record Symbol(string Name, bool IsQuoted, LineInfo Info)
        : Value(Info)
    {
        public sealed override string ToString() => $"{(IsQuoted ? "'" : "")}{Name}";
    }

    public sealed record String(string Value, LineInfo Info)
        : Value(Info)
    {
        public sealed override string ToString() => $"\"{Value}\"";
    }

    public sealed record List(List<Value> Items, LineInfo Info)
        : Value(Info)
    {
        public sealed override string ToString() => $"[{string.Join(", ", Items)}]";
    }

    public sealed record Tuple(List<Value> Items, bool IsQuoted, LineInfo Info)
        : Value(Info)
    {
        public sealed override string ToString() => $"{(IsQuoted ? "'" : "")}({string.Join(", ", Items)})";
    }
}
