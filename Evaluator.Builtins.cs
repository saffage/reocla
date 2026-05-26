using System.Diagnostics;
using System.Text;

namespace Reocla;

public static class Builtins
{
    public const string IndexErrorTag = "index-error";
    public const string ParseErrorTag = "parse-error";
    public const string NoMatchErrorTag = "no-match";
    public const string FileNotExistErrorTag = "file-not-exist";
    public const string ImportFileNotExistErrorTag = "import-file-not-exist";
    public const string FileNotFoundErrorTag = "file-not-found";
    public const string PermissionDeniedErrorTag = "permission-denied";
    public const string IoErrorTag = "io-error";
    public const string DivByZeroErrorTag = "div-by-zero";

    public static void RegisterFor(Evaluator context)
    {
        void Register(string name, Action<Evaluator> proc) =>
            context.Procedures[name] = proc;

        // Arithmetic operations
        Register("+", BuiltinArithmeticalOp);
        Register("-", BuiltinArithmeticalOp);
        Register("*", BuiltinArithmeticalOp);
        Register("/", BuiltinArithmeticalOp);
        Register("%", BuiltinArithmeticalOp);

        // Comparison operations
        Register("=", BuiltinComparisonOp);
        Register("<>", BuiltinComparisonOp);
        Register(">=", BuiltinComparisonOp);
        Register("<=", BuiltinComparisonOp);
        Register(">", BuiltinComparisonOp);
        Register("<", BuiltinComparisonOp);

        // Boolean operators
        Register("not", BuiltinBoolOp);
        Register("and", BuiltinBoolOp);
        Register("or", BuiltinBoolOp);

        // Sequence operators
        Register("|", BuiltinConcat);
        Register("::", BuiltinCons);
        Register("@", BuiltinGet);
        Register("->", BuiltinAppend);
        Register("<-", BuiltinPrepend);
        Register("len", BuiltinLen);

        // Magic
        Register("eval", BuiltinEval);
        Register("catch", BuiltinCatch);
        Register("throw", BuiltinThrow);
        Register("match", BuiltinMatch);

        // itoa/atoi
        Register("str", BuiltinToStr);
        Register("int", BuiltinToInt);

        // I/O
        Register("print", (e) => BuiltinPrint(e, false));
        Register("println", (e) => BuiltinPrint(e, true));
        Register("proc", BuiltinDefineProc);
        Register("if", BuiltinIf);
        Register("if-else", BuiltinIf);
        Register("while", BuiltinWhile);

        // Files I/O
        Register("read-file", BuiltinReadFile);
        Register("read-lines", BuiltinReadLines);
        Register("write-file", BuiltinWriteFile);
        Register("write-lines", BuiltinWriteLines);
        Register("write-stack", BuiltinWriteStack);
        Register("import", BuiltinImport);
    }

    public static void BuiltinArithmeticalOp(Evaluator e)
    {
        var b = e.Pop();
        var a = e.Pop();

        if (a is not Value.Number x || b is not Value.Number y)
        {
            throw e.TypeMismatch($"arithmetic requires both operands to be integers, got '{a.GetType().Name}' and '{b.GetType().Name}'");
        }

        if ((e.CurrentProcName == "/" || e.CurrentProcName == "%") && y.Value == 0)
        {
            e.Throw(DivByZeroErrorTag);
        }

        long result = e.CurrentProcName switch
        {
            "+" => x.Value + y.Value,
            "-" => x.Value - y.Value,
            "*" => x.Value * y.Value,
            "/" => x.Value / y.Value,
            "%" => x.Value % y.Value,
            _ => throw new UnreachableException($"Unexpected operation '{e.CurrentProcName}'")
        };
        e.Push(new Value.Number(result, e.CurrentProcInfo));
    }

    public static void BuiltinComparisonOp(Evaluator e)
    {
        static int? Compare(Value a, Value b) => (a, b) switch
        {
            (Value.String x, Value.String y) => x.Value.CompareTo(y.Value),
            (Value.Number x, Value.Number y) => x.Value.CompareTo(y.Value),
            (Value.Char x, Value.Char y) => x.Value.CompareTo(y.Value),
            (Value.Bool x, Value.Bool y) => x.Value.CompareTo(y.Value),
            (Value.List x, Value.List y) => (x == y) ? 0 : 1,
            (Value.Tuple x, Value.Tuple y) => (x != y) ? 0 : 1,
            (_, _) => null,
        };

        var b = e.Pop();
        var a = e.Pop();
        var cmp = Compare(a, b) ??
            throw e.TypeMismatch(
                $"cannot compare '{a.GetType().Name}' and '{b.GetType().Name}'",
                location: e.CurrentProcInfo);

        bool result = e.CurrentProcName switch
        {
            "=" => cmp == 0,
            "<>" => cmp != 0,
            ">=" => cmp >= 0,
            "<=" => cmp <= 0,
            ">" => cmp > 0,
            "<" => cmp < 0,
            _ => throw new UnreachableException($"Unexpected operation '{e.CurrentProcName}'")
        };
        e.Push(new Value.Bool(result, e.CurrentProcInfo));
    }

    public static void BuiltinBoolOp(Evaluator e)
    {
        if (e.CurrentProcName == "not")
        {
            var a = e.Pop();

            if (a is Value.Bool ab)
            {
                e.Push(new Value.Bool(!ab.Value, e.CurrentProcInfo));
            }
            else
            {
                throw e.TypeMismatch($"expected 'Bool', got '{a.GetType().Name}'");
            }
        }
        else
        {
            var b = e.Pop();
            var a = e.Pop();

            if (a is Value.Bool ab && b is Value.Bool bb)
            {
                bool result = e.CurrentProcName switch
                {
                    "and" => ab.Value && bb.Value,
                    "or" => ab.Value || bb.Value,
                    _ => throw new UnreachableException($"Unexpected operation '{e.CurrentProcName}'")
                };
                e.Push(new Value.Bool(result, e.CurrentProcInfo));
            }
            else
            {
                throw e.TypeMismatch($"expected both operand to be of type 'Bool', got '{a.GetType().Name}' and '{b.GetType().Name}'");
            }
        }
    }

    public static void BuiltinConcat(Evaluator e)
    {
        var b = e.Pop();
        var a = e.Pop();

        if (a is Value.List la && b is Value.List lb)
        {
            var newItems = new List<Value>(la.Items);
            newItems.AddRange(lb.Items);
            e.Push(new Value.List(newItems, e.CurrentProcInfo));
        }
        else if (a is Value.Tuple ta && b is Value.Tuple tb)
        {
            var newItems = new List<Value>(ta.Items);
            newItems.AddRange(tb.Items);
            e.Push(new Value.Tuple(newItems, ta.IsQuoted, e.CurrentProcInfo));
        }
        else if (a is Value.String sa && b is Value.String sb)
        {
            e.Push(new Value.String(sa.Value + sb.Value, e.CurrentProcInfo));
        }
        else if (a is Value.Char ca && b is Value.String sb2)
        {
            e.Push(new Value.String(ca.Value + sb2.Value, e.CurrentProcInfo));
        }
        else if (a is Value.String sa2 && b is Value.Char cb)
        {
            e.Push(new Value.String(sa2.Value + cb.Value, e.CurrentProcInfo));
        }
        else
        {
            throw e.TypeMismatch(
                "Expected both operand to be 'List', 'Tuple' or 'String', got " +
                $"'{a.GetType().Name}' and '{b.GetType().Name}'");
        }
    }

    public static void BuiltinPrint(Evaluator e, bool newline)
    {
        var obj = e.Peek();
        var msg = obj is Value.String s ? s.Value : obj.ToString();

        if (newline)
        {
            Console.WriteLine(msg);
        }
        else
        {
            Console.Write(msg);
        }
        Console.Out.Flush();
    }

    public static void BuiltinDefineProc(Evaluator e)
    {
        var name_ = e.Pop();
        var body = e.Pop();

        if (name_ is not Value.Symbol name)
        {
            throw e.TypeMismatch("proc name must be a 'Symbol'");
        }
        if (body is not Value.List)
        {
            throw e.TypeMismatch("proc body must be a 'List'");
        }
        e.RegisterProc(name.Name, body, e.Scope);
    }

    public static void BuiltinIf(Evaluator e)
    {
        Value? elseBranch = e.CurrentProcName == "if-else"
            ? e.Pop()
            : null;

        var ifBranch = e.Pop();
        var cond = e.Pop();

        if (ifBranch is not Value.List)
        {
            throw e.TypeMismatch("then branch must be a 'List'");
        }
        if (cond is not Value.List)
        {
            throw e.TypeMismatch("condition must be a 'List'");
        }

        e.Eval(cond);

        if (e.Pop() is Value.Bool res)
        {
            if (res.Value)
            {
                e.Eval(ifBranch);
            }
            else if (elseBranch is not null)
            {
                if (elseBranch is not Value.List)
                {
                    throw e.TypeMismatch("else branch must be a 'List'");
                }
                e.Eval(elseBranch);
            }
        }
        else
        {
            throw e.TypeMismatch("condition must push a 'Bool' value");
        }
    }

    public static void BuiltinWhile(Evaluator e)
    {
        var body = e.Pop();
        var cond = e.Pop();

        if (body is not Value.List)
        {
            throw e.TypeMismatch("expected body be of type 'List'");
        }
        if (cond is not Value.List)
        {
            throw e.TypeMismatch("expected condition be of type 'List'");
        }

        while (true)
        {
            e.Eval(cond);

            if (e.Pop() is Value.Bool b)
            {
                if (!b.Value) break;
                e.Eval(body);
            }
            else
            {
                throw e.TypeMismatch("condition must push a 'Bool'");
            }
        }
    }

    public static void BuiltinGet(Evaluator e)
    {
        var indexValue = e.Pop();
        var seq = e.Pop();

        if (indexValue is not Value.Number index)
        {
            throw e.TypeMismatch("index must be of type 'Int'");
        }

        int i = (int)index.Value;
        if (i < 0)
        {
            e.Throw(IndexErrorTag);
            return;
        }

        if (seq is Value.List list)
        {
            if (i < list.Items.Count)
            {
                e.Push(list.Items[i]);
            }
            else
            {
                e.Throw(IndexErrorTag);
            }
        }
        else if (seq is Value.Tuple tuple)
        {
            if (i < tuple.Items.Count)
            {
                e.Push(tuple.Items[i]);
            }
            else
            {
                e.Throw(IndexErrorTag);
            }
        }
        else if (seq is Value.String str)
        {
            if (i < str.Value.Length)
            {
                e.Push(new Value.Char(str.Value[i], e.CurrentProcInfo));
            }
            else
            {
                e.Throw(IndexErrorTag);
            }
        }
        else
        {
            throw e.TypeMismatch("expected 'List', 'Tuple' or 'String' to index");
        }
    }

    public static void BuiltinAppend(Evaluator e)
    {
        var itemValue = e.Pop();
        var listValue = e.Pop();

        if (listValue is Value.List list)
        {
            var newItems = new List<Value>(list.Items) { itemValue };
            var newList = new Value.List(newItems, e.CurrentProcInfo);
            e.Push(newList);
        }
        else if (listValue is Value.String str)
        {
            if (itemValue is Value.String suffix)
            {
                e.Push(new Value.String(str.Value + suffix.Value, e.CurrentProcInfo));
            }
            else if (itemValue is Value.Char c)
            {
                e.Push(new Value.String(str.Value + c.Value, e.CurrentProcInfo));
            }
            else
            {
                throw e.TypeMismatch($"cannot append '{itemValue.GetType().Name}' to 'String'");
            }
        }
        else
        {
            throw e.TypeMismatch($"expected 'List' or 'String', got '{listValue.GetType().Name}'");
        }
    }

    public static void BuiltinPrepend(Evaluator e)
    {
        var itemValue = e.Pop();
        var listValue = e.Pop();

        if (listValue is Value.List list)
        {
            var newItems = new List<Value>(list.Items);
            newItems.Insert(0, itemValue);
            var newList = new Value.List(newItems, e.CurrentProcInfo);
            e.Push(newList);
        }
        else if (listValue is Value.String str)
        {
            if (itemValue is Value.String prefix)
            {
                e.Push(new Value.String(prefix.Value + str.Value, e.CurrentProcInfo));
            }
            else if (itemValue is Value.Char c)
            {
                e.Push(new Value.String(c.Value + str.Value, e.CurrentProcInfo));
            }
            else
            {
                throw e.TypeMismatch($"cannot prepend '{itemValue.GetType().Name}' to 'String'");
            }
        }
        else
        {
            throw e.TypeMismatch($"expected 'List' or 'String', got '{listValue.GetType().Name}'");
        }
    }

    public static void BuiltinLen(Evaluator e)
    {
        int len = e.Pop() switch
        {
            Value.List l => l.Items.Count,
            Value.Tuple t => t.Items.Count,
            Value.String s => s.Value.Length,
            var obj => throw e.TypeMismatch($"expected 'List', 'Tuple' or 'String', got '{obj.GetType().Name}'"),
        };
        e.Push(new Value.Number(len, e.CurrentProcInfo));
    }

    public static void BuiltinHead(Evaluator e)
    {
        var seq = e.Pop();

        if (seq is Value.List list)
        {
            if (list.Items.Count == 0)
            {
                e.Throw(IndexErrorTag);
                return;
            }
            e.Push(list.Items[0]);
        }
        else if (seq is Value.Tuple tuple)
        {
            if (tuple.Items.Count == 0)
            {
                e.Throw(IndexErrorTag);
                return;
            }
            e.Push(tuple.Items[0]);
        }
        else if (seq is Value.String str)
        {
            if (str.Value.Length == 0)
            {
                e.Throw(IndexErrorTag);
                return;
            }
            e.Push(new Value.Char(str.Value[0], e.CurrentProcInfo));
        }
        else
        {
            e.TypeMismatch("expected 'List', 'Tuple' or 'String'");
        }
    }

    public static void BuiltinTail(Evaluator e)
    {
        var seq = e.Pop();

        if (seq is Value.List list)
        {
            if (list.Items.Count == 0)
            {
                e.Throw(IndexErrorTag);
                return;
            }
            e.Push(new Value.List(list.Items.Skip(1).ToList(), e.CurrentProcInfo));
        }
        else if (seq is Value.Tuple tuple)
        {
            if (tuple.Items.Count == 0)
            {
                e.Throw(IndexErrorTag);
                return;
            }
            e.Push(new Value.Tuple(tuple.Items.Skip(1).ToList(), tuple.IsQuoted, e.CurrentProcInfo));
        }
        else if (seq is Value.String str)
        {
            if (str.Value.Length == 0)
            {
                e.Throw(IndexErrorTag);
                return;
            }
            e.Push(new Value.String(str.Value[1..], e.CurrentProcInfo));
        }
        else
        {
            e.TypeMismatch("expected 'List', 'Tuple' or 'String'");
        }
    }

    public static void BuiltinCons(Evaluator e)
    {
        var seq = e.Pop();

        if (seq is Value.List list)
        {
            if (list.Items.Count == 0)
            {
                e.Throw(IndexErrorTag);
                return;
            }
            e.Push(list.Items[0]);
            e.Push(new Value.List([.. list.Items.Skip(1)], e.CurrentProcInfo));
        }
        else if (seq is Value.Tuple tuple)
        {
            if (tuple.Items.Count == 0)
            {
                e.Throw(IndexErrorTag);
                return;
            }
            e.Push(tuple.Items[0]);
            e.Push(new Value.Tuple([.. tuple.Items.Skip(1)], tuple.IsQuoted, e.CurrentProcInfo));
        }
        else if (seq is Value.String str)
        {
            if (str.Value.Length == 0)
            {
                e.Throw(IndexErrorTag);
                return;
            }
            e.Push(new Value.Char(str.Value[0], e.CurrentProcInfo));
            e.Push(new Value.String(new string([.. str.Value.Skip(1)]), e.CurrentProcInfo));
        }
        else
        {
            throw e.TypeMismatch("expected 'List', 'Tuple' or 'String'");
        }
    }

    public static void BuiltinEval(Evaluator e)
    {
        if (e.Pop() is Value.List list)
        {
            e.Eval(list);
        }
        else
        {
            throw e.TypeMismatch("expected 'List' to evaluate");
        }
    }

    public static void BuiltinCatch(Evaluator e)
    {
        var handlersValue = e.Pop();
        var tryBlockValue = e.Pop();

        if (handlersValue is not Value.Tuple handlers || handlers.IsQuoted)
        {
            throw e.TypeMismatch("expected handlers as unquoted 'Tuple'");
        }
        if (tryBlockValue is not Value.List tryBlock)
        {
            throw e.TypeMismatch("expected try block 'List'");
        }

        var oldHandlers = new Dictionary<string, Value>(e.Handlers);

        for (int i = 0; i < handlers.Items.Count; i += 2)
        {
            var handlerValue = handlers.Items[i];
            var tagValue = (i + 1) < handlers.Items.Count ? handlers.Items[i + 1] : null;

            if (handlerValue is not Value.List handler)
            {
                throw e.TypeMismatch($"handler is not a 'List' but '{handlerValue.GetType().Name}'");
            }
            if (tagValue is not null)
            {
                if (tagValue is not Value.Symbol tag || !tag.IsQuoted)
                {
                    throw e.TypeMismatch($"tag is not a 'Symbol' but '{tagValue.GetType().Name}'");
                }
                e.Handlers[tag.Name] = handler;
            }
            else
            {
                e.UniversalHandler = handler;
            }
        }

        e.Eval(tryBlock);
        e.Handlers = oldHandlers;
    }

    public static void BuiltinThrow(Evaluator e)
    {
        if (e.Pop() is Value.Symbol sym && !sym.IsQuoted)
        {
            e.Throw(sym.Name);
        }
        else
        {
            throw e.TypeMismatch("expected exception tag as 'Symbol'");
        }
    }

    public static void BuiltinMatch(Evaluator e)
    {
        var clausesValue = e.Pop();
        var subjectValue = e.Pop();

        if (clausesValue is not Value.Tuple clauses)
        {
            throw e.TypeMismatch("expected clauses as unquoted 'Tuple'");
        }
        if (subjectValue is not Value.Symbol subject)
        {
            throw e.TypeMismatch("expected subject as 'Symbol'");
        }

        for (int i = 0; i < clauses.Items.Count; i += 2)
        {
            var clauseValue = clauses.Items[i];
            var tagValue = (i + 1) < clauses.Items.Count ? clauses.Items[i + 1] : null;

            if (clauseValue is not Value.List clause)
            {
                throw e.TypeMismatch($"expected clause to be a 'List', got '{clauseValue.GetType().Name}'");
            }
            if (tagValue is not null)
            {
                if (tagValue is not Value.Symbol tag || !tag.IsQuoted)
                {
                    throw e.TypeMismatch($"expected tag to be a 'Symbol', got '{tagValue.GetType().Name}'");
                }
                if (subject.Name == tag.Name)
                {
                    e.Eval(clause);
                    return;
                }
            }
            else
            {
                // Catch all clause.
                e.Eval(clause);
                return;
            }
        }
        e.Throw(NoMatchErrorTag);
    }

    public static void BuiltinToStr(Evaluator e)
    {
        var obj = e.Pop();
        var str = obj is Value.String s
            ? s
            : new Value.String(obj.ToString(), e.CurrentProcInfo);
        e.Push(str);
    }

    public static void BuiltinToInt(Evaluator e)
    {
        if (e.Pop() is Value.String s)
        {
            if (long.TryParse(s.Value, out long parsed))
            {
                e.Push(new Value.Number(parsed, e.CurrentProcInfo));
            }
            else
            {
                e.Throw(ParseErrorTag);
            }
        }
        else
        {
            throw e.TypeMismatch("expected 'String'");
        }
    }

    public static void BuiltinReadFile(Evaluator e) => FileOp(e, (p) =>
    {
        string content = File.ReadAllText(p);
        e.Push(new Value.String(content, e.CurrentProcInfo));
    });

    public static void BuiltinReadLines(Evaluator e) => FileOp(e, (p) =>
    {
        var lines = File.ReadAllLines(p);
        var objs = lines
            .Select(line => new Value.String(line, e.CurrentProcInfo) as Value)
            .ToList();
        e.Push(new Value.List(objs, e.CurrentProcInfo));
    });

    public static void BuiltinWriteFile(Evaluator e)
    {
        var pathValue = e.Pop();
        var contentValue = e.Pop();

        if (pathValue is not Value.String path)
        {
            throw e.TypeMismatch("expected filepath as 'String'");
        }
        if (contentValue is not Value.String content)
        {
            throw e.TypeMismatch("expected content as 'String'");
        }
        WriteFileSafe(e, path.Value, content.Value);
    }

    public static void BuiltinWriteLines(Evaluator e)
    {
        var pathValue = e.Pop();
        var linesValue = e.Pop();

        if (pathValue is not Value.String path)
        {
            throw e.TypeMismatch("expected filepath as 'String'");
        }
        if (linesValue is not Value.List lines)
        {
            throw e.TypeMismatch("expected lines of lines as 'List'");
        }

        var buf = new StringBuilder();

        foreach (var lineValue in lines.Items)
        {
            if (lineValue is not Value.String line)
            {
                throw e.TypeMismatch("expected line as 'String'", location: linesValue.LineInfo);
            }
            buf.Append(line.Value);
        }
        WriteFileSafe(e, path.Value, buf.ToString());
    }

    public static void BuiltinWriteStack(Evaluator e)
    {
        Console.Write("Stack: ");
        Console.WriteLine(string.Join(", ", e.Stack.AsEnumerable().Reverse()));
    }

    public static void BuiltinImport(Evaluator e)
    {
        static string RelativeToDir(string dir, string path)
        {
            if (Uri.TryCreate(new Uri(dir), path, out var final))
            {
                return final.LocalPath;
            }
            else
            {
                return path;
            }
        }

        string importFilename = (e.Pop() as Value.String ?? throw e.TypeMismatch("Expected filepath")).Value;

        if (!string.IsNullOrEmpty(e.Filename))
        {
            string currentFileDir = Path.GetFullPath(e.Filename ?? ".");
            importFilename = RelativeToDir(currentFileDir, importFilename);
        }

        if (!File.Exists(importFilename))
        {
            e.Throw(ImportFileNotExistErrorTag);
            return;
        }

        string content = File.ReadAllText(importFilename);
        var obj = Parser.Parse(content, importFilename);

        var importContext = new Evaluator
        {
            Filename = importFilename
        };
        importContext.Eval(obj);

        // Add all procedures to the current context.
        foreach (var (procName, proc) in importContext.Procedures)
        {
            _ = e.Procedures.TryAdd(procName, proc);
        }
    }

    private static void FileOp(Evaluator e, Action<string> op)
    {
        var pathValue = e.Pop();

        if (pathValue is not Value.String path)
        {
            throw e.TypeMismatch("expected filepath as 'String'");
        }

        try
        {
            op(path.Value);
        }
        catch (FileNotFoundException)
        {
            e.Throw(FileNotFoundErrorTag);
        }
        catch (UnauthorizedAccessException)
        {
            e.Throw(PermissionDeniedErrorTag);
        }
        catch
        {
            e.Throw(IoErrorTag);
        }
    }

    private static void WriteFileSafe(Evaluator e, string path, string content)
    {
        try
        {
            File.WriteAllText(path, content);
        }
        catch (IOException)
        {
            e.Throw(FileNotExistErrorTag);
        }
        catch
        {
            e.Throw(IoErrorTag);
        }
    }
}
