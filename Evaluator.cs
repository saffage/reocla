using System.Diagnostics;

namespace Reocla;

public class Evaluator
{
    public string Filename { get; set; } = "";

    public CallSite? CurrentCall
    {
        get
        {
            _ = CallStack.TryPeek(out var call);
            return call;
        }
    }

    public string? CurrentProcName => CurrentCall?.ProcName;
    public LineInfo CurrentProcInfo => CurrentCall?.LineInfo ?? new(Filename, 1, 1);

    public Stack<Value> Stack { get; private set; } = [];
    public Stack<CallSite> CallStack { get; private set; } = [];

    public Value? UniversalHandler { get; set; }
    public Dictionary<string, Value> Handlers { get; set; } = [];
    public Dictionary<string, Value> Scope { get; set; } = [];
    public Dictionary<string, Action<Evaluator>> Procedures { get; set; } = [];

    public Evaluator()
    {
        Builtins.RegisterFor(this);
    }

    public void RegisterProc(string name, Value body, Dictionary<string, Value> frame)
    {
        Procedures[name] = (context) =>
        {
            var oldFrame = context.Scope;
            context.Scope = new Dictionary<string, Value>(frame);
            context.Eval(body);
            context.Scope = oldFrame;
        };
    }

    public void Push(Value o)
    {
        Stack.Push(o);
    }

    public Value Pop()
    {
        if (!Stack.TryPop(out var value))
        {
            throw Error("stack underflow");
        }
        return value;
    }

    public Value Peek()
    {
        if (!Stack.TryPeek(out var value))
        {
            throw Error("stack is empty");
        }
        return value;
    }

    public void CallProc(string name, LineInfo info, Action<Evaluator> proc)
    {
        CallStack.Push(new(name, info));
        proc(this);
        CallStack.Pop();
    }

    public void Eval(Value rootObj)
    {
        if (rootObj is not Value.List list)
        {
            throw Error("root object must be List");
        }

        foreach (var obj in list.Items)
        {
            if (obj is Value.Tuple tuple)
            {
                if (tuple.IsQuoted)
                {
                    UnquoteAndPush(tuple);
                }
                else
                {
                    if (Stack.Count < tuple.Items.Count)
                    {
                        throw Error($"not enough items on stack for capture, got {Stack.Count}, want {tuple.Items.Count}", location: tuple.LineInfo);
                    }
                    for (int i = tuple.Items.Count - 1; i >= 0; i--)
                    {
                        if (tuple.Items[i] is not Value.Symbol s)
                        {
                            throw Error("capture target must be Symbol", location: tuple.Items[i].LineInfo);
                        }
                        Scope[s.Name] = Pop();
                    }
                }
            }
            else if (obj is Value.Symbol sym)
            {
                if (sym.IsQuoted)
                {
                    UnquoteAndPush(sym);
                }
                else
                {
                    EvalSymbol(sym);
                }
            }
            else
            {
                Push(obj);
            }
        }
    }

    private void UnquoteAndPush(Value obj)
    {
        if (obj is Value.Tuple t)
        {
            Push(t with { IsQuoted = false });
        }
        else if (obj is Value.Symbol s)
        {
            Push(s with { IsQuoted = false });
        }
        else
        {
            throw new UnreachableException($"invalid quoted object {obj.GetType().Name}");
        }
    }

    private void EvalSymbol(Value.Symbol sym)
    {
        if (sym.Name.StartsWith('$'))
        {
            if (!Scope.TryGetValue(sym.Name[1..], out var value))
            {
                throw Error($"unbound local variable: {sym.Name[1..]}");
            }
            Push(value);
        }
        else
        {
            if (!Procedures.TryGetValue(sym.Name, out var proc))
            {
                throw Error($"unbound procedure: {sym.Name}");
            }
            CallProc(sym.Name, sym.LineInfo, proc);
        }
    }

    public void Throw(string tag)
    {
        if (Handlers.TryGetValue(tag, out var handler))
        {
            Eval(handler);
        }
        else if (UniversalHandler is not null)
        {
            Eval(UniversalHandler);
        }
        else
        {
            throw Error($"unhandled exception: {tag}");
        }
    }

    public EvalException Error(string message, LineInfo? location = null)
        => new(message, location ?? CurrentProcInfo, new Stack<CallSite>(CallStack));

    public TypeException TypeMismatch(string message, LineInfo? location = null)
        => new(message, location ?? CurrentProcInfo, new Stack<CallSite>(CallStack));
}

public sealed record CallSite(string ProcName, LineInfo LineInfo)
{
    public sealed override string ToString() => $"occurred in call to '{ProcName}' at {LineInfo}";
    public string Suffix => $"\n\t{this}";
}

public class EvalException(string message, LineInfo? location = null, Stack<CallSite>? callStack = null)
    : Exception($"{location?.Prefix ?? ""}{message}{(callStack is null ? "" : "\n    ")}{string.Join("\n    ", callStack ?? [])}")
{
}

public class TypeException(string message, LineInfo? location = null, Stack<CallSite>? callStack = null)
    : Exception($"{location?.Prefix ?? ""}type mismatch, {message}{(callStack is null ? "" : "\n    ")}{string.Join("\n    ", callStack ?? [])}")
{
}
