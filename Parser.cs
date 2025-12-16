using System.Text;

namespace Reocla;

public class ParseException(string message, string filename, uint pos, uint line, uint column)
    : Exception($"{filename}:{line}:{column}: {message}")
{
    public string Filename { get; init; } = filename;
    public uint Pos { get; init; } = pos;
    public uint Line { get; init; } = line;
    public uint Column { get; init; } = column;
}

public class Parser
{
    private readonly string _source;
    private readonly string _filename;

    private uint _pos;
    private uint _line;
    private uint _column;

    private const string SymbolChars = "_@$+-*/=?!%><&|~:";

    private bool IsEOF => _pos >= _source.Length;

    public static Value.List Parse(string input, string filename = "<stdin>")
    {
        var parser = new Parser(input, filename);
        var items = parser.ParseObjectSequence();
        return new Value.List(items, new LineInfo(filename, 1, 1));
    }

    private Parser(string source, string filename)
    {
        _source = source;
        _filename = filename;
        _pos = 0;
        _line = 1;
        _column = 1;
    }

    // --- Core Parsing Logic ---

    private List<Value> ParseObjectSequence()
    {
        List<Value> list = [];

        while (true)
        {
            SkipWhitespace();
            if (IsEOF || Peek() == ']' || Peek() == ')')
            {
                break;
            }
            list.Add(ParseObject());
        }

        return list;
    }

    private Value ParseObject()
    {
        var info = CurrentLineInfo();
        char c = Peek();

        // Int.
        if (char.IsDigit(c) || (c == '-' && char.IsDigit(Peek(1))))
        {
            return ParseInt();
        }

        // Char, quoted symbol or tuple.
        if (c == '\'')
        {
            if (IsCharLiteral())
            {
                return ParseChar();
            }

            // Consume the quote.
            Advance();

            if (Peek() == '(')
            {
                return ParseTuple(quoted: true, info);
            }

            return ParseSymbol(quoted: true, info);
        }

        return c switch
        {
            '[' => ParseList(),
            '(' => ParseTuple(quoted: false, info),
            '"' => ParseString(),
            '#' => ParseBool(),
            _ => ParseSymbol(quoted: false, info)
        };
    }

    // --- Primitive Parsers ---

    private Value.Number ParseInt()
    {
        uint start = _pos;
        var info = CurrentLineInfo();

        // Handle optional sign.
        if (Peek() == '-') Advance();

        // Consume digits.
        while (!IsEOF && char.IsDigit(Peek()))
            Advance();

        // Use Span to parse without allocating a substring.
        var span = _source.AsSpan((int)start, (int)(_pos - start));

        if (long.TryParse(span, out long result))
        {
            return new Value.Number(result, info);
        }

        throw Error($"Invalid integer format: {span}");
    }

    private Value.Char ParseChar()
    {
        var info = CurrentLineInfo();

        Consume('\'');
        char c = Advance();

        if (c == '\\')
        {
            c = ParseEscapedChar();
        }

        Consume('\'');
        return new Value.Char(c, info);
    }

    private Value.List ParseList()
    {
        var info = CurrentLineInfo();

        Consume('[');
        var items = ParseObjectSequence();
        Consume(']');
        return new Value.List(items, info);
    }

    private Value.Tuple ParseTuple(bool quoted, LineInfo info)
    {
        Consume('(');
        var items = ParseObjectSequence();
        Consume(')');
        return new Value.Tuple(items, quoted, info);
    }

    private Value.String ParseString()
    {
        var info = CurrentLineInfo();

        Consume('"');
        var str = new StringBuilder();

        while (!IsEOF && Peek() != '"')
        {
            char c = Advance();
            if (c == '\\')
                c = ParseEscapedChar();

            str.Append(c);
        }

        Consume('"');
        return new Value.String(str.ToString(), info);
    }

    private Value.Bool ParseBool()
    {
        var info = CurrentLineInfo();

        Consume('#');
        return Advance() switch
        {
            't' => new Value.Bool(true, info),
            'f' => new Value.Bool(false, info),

            // TODO: escape char.
            var c => throw Error($"Expected #t or #f for boolean, got '{c}'")
        };
    }

    private Value.Symbol ParseSymbol(bool quoted, LineInfo info)
    {

        uint start = _pos;

        while (!IsEOF)
        {
            char c = Peek();

            if (char.IsLetterOrDigit(c) || SymbolChars.Contains(c))
            {
                Advance();
            }
            else
            {
                break;
            }
        }

        // If we didn't advance at all, it wasn't a valid symbol.
        if (_pos == start)
        {
            throw Error("Unexpected character or empty symbol");
        }

        string name = _source[(int)start..(int)_pos];
        return new Value.Symbol(name, quoted, info);
    }

    // --- Helpers ---

    private LineInfo CurrentLineInfo() => new(_filename, _line, _column);

    private ParseException Error(string message) => new(message, _filename, _pos, _line, _column);

    private char ParseEscapedChar() => Advance() switch
    {
        'n' => '\n',
        'r' => '\r',
        't' => '\t',
        '\\' => '\\',
        '\'' => '\'',
        '"' => '"',
        var c => c
    };

    /// <summary>
    /// Heuristic to determine if ' is starting a char literal ('x')
    /// or a quoted symbol ('foo)
    /// </summary>
    private bool IsCharLiteral() =>
        // '\n' (length 4 from start).
        Peek(1) == '\\' && Peek(3) == '\'' ||
        // 'a' (length 3 from start).
        Peek(2) == '\'';

    private void SkipWhitespace()
    {
        while (!IsEOF)
        {
            char c = Peek();

            if (char.IsWhiteSpace(c))
            {
                Advance();
            }
            else if (c == ';') // Line comment
            {
                while (!IsEOF && Peek() != '\n' && Peek() != '\r')
                {
                    Advance();
                }
            }
            else if (c == '(' && Peek(1) == '*') // Block comment (* ... *)
            {
                Advance(); Advance(); // Eat (*
                while (!IsEOF)
                {
                    if (Peek() == '*' && Peek(1) == ')')
                    {
                        Advance(); Advance(); // Eat *)
                        break;
                    }
                    Advance();
                }
            }
            else
            {
                break;
            }
        }
    }

    // --- Cursor Management ---

    private char Peek(int offset = 0)
    {
        if (_pos + offset >= _source.Length)
        {
            return '\0';
        }
        return _source[(int)_pos + offset];
    }

    private char Advance()
    {
        if (IsEOF)
        {
            return '\0';
        }

        char c = _source[(int)_pos++];

        // Update Line/Column info
        if (c == '\n')
        {
            _line++;
            _column = 1;
        }
        else
        {
            _column++;
        }

        return c;
    }

    private void Consume(char expected)
    {
        if (IsEOF || _source[(int)_pos] != expected)
        {
            throw Error($"Expected {expected}");
        }
        Advance();
    }
}
