namespace VbFlagResolver.Core;

public enum LogicOp { Imp, Eqv, Xor, Or, And }

/// <summary>Koşul ifadesi ağacı. Her düğüm kaynak metnini taşır; değişmeyen düğümler aynen yazılır.</summary>
public abstract record Expr
{
    /// <summary>Kaynaktaki orijinal metin (sadeleştirme sonrası üretilen düğümlerde null).</summary>
    public string? Source { get; init; }
}

public sealed record BoolLit(bool Value) : Expr;

/// <summary>Tek bir tanımlayıcı (bayrak olabilir). Name: modül öneki olmadan isim.</summary>
public sealed record Ident(string Name) : Expr;

public sealed record NotExpr(Expr Operand) : Expr;

public sealed record Binary(LogicOp Op, Expr Left, Expr Right) : Expr;

/// <summary>Karşılaştırma: =, &lt;&gt;, &lt;, &gt;, &lt;=, &gt;=, Like, Is.</summary>
public sealed record Compare(string Op, Expr Left, Expr Right) : Expr;

public sealed record Paren(Expr Inner) : Expr;

/// <summary>Mantık dışı ifade (aritmetik, fonksiyon çağrısı, string...). Metni aynen korunur.</summary>
public sealed record Opaque(string Text, bool HasCall) : Expr;

public sealed class ExprParseException(string message) : Exception(message);

/// <summary>
/// Koşul ifadesini AST'ye çevirir. Öncelik (yüksekten düşüğe):
/// aritmetik (opak) &gt; karşılaştırma &gt; Not &gt; And &gt; Or &gt; Xor &gt; Eqv &gt; Imp.
/// </summary>
public sealed class ExprParser
{
    private static readonly string[] CompareOps = ["=", "<>", "<", ">", "<=", ">=", "=<", "=>"];
    private static readonly string[] LogicalWords = ["And", "Or", "Xor", "Eqv", "Imp", "Not"];

    private readonly List<Token> _tokens;
    private readonly string _text;
    private readonly ISet<string>? _moduleNames;
    private int _pos;

    private ExprParser(List<Token> tokens, string text, ISet<string>? moduleNames)
    {
        _tokens = tokens;
        _text = text;
        _moduleNames = moduleNames;
    }

    /// <summary>tokens: yalnızca ifade token'ları; text: token konumlarının ait olduğu metin.</summary>
    public static Expr Parse(List<Token> tokens, string text, ISet<string>? moduleNames = null)
    {
        if (tokens.Count == 0) throw new ExprParseException("Boş ifade");
        var p = new ExprParser(tokens, text, moduleNames);
        var e = p.ParseLevel(0);
        if (p._pos < tokens.Count) throw new ExprParseException($"Beklenmeyen token: {tokens[p._pos].Text}");
        return e;
    }

    public static Expr Parse(string text, ISet<string>? moduleNames = null) => Parse(Lexer.Tokenize(text), text, moduleNames);

    private Token? Peek => _pos < _tokens.Count ? _tokens[_pos] : null;

    private string Span(int fromTok, int toTokExclusive)
        => _text[_tokens[fromTok].Start.._tokens[toTokExclusive - 1].End];

    // seviye 0: Imp, 1: Eqv, 2: Xor, 3: Or, 4: And
    private static readonly (string word, LogicOp op)[] Levels =
        [("Imp", LogicOp.Imp), ("Eqv", LogicOp.Eqv), ("Xor", LogicOp.Xor), ("Or", LogicOp.Or), ("And", LogicOp.And)];

    private Expr ParseLevel(int level)
    {
        if (level == Levels.Length) return ParseNot();
        int start = _pos;
        var left = ParseLevel(level + 1);
        while (Peek is { } t && t.Is(Levels[level].word))
        {
            _pos++;
            var right = ParseLevel(level + 1);
            left = new Binary(Levels[level].op, left, right) { Source = Span(start, _pos) };
        }
        return left;
    }

    private Expr ParseNot()
    {
        if (Peek is { } t && t.Is("Not"))
        {
            int start = _pos;
            _pos++;
            var operand = ParseNot();
            return new NotExpr(operand) { Source = Span(start, _pos) };
        }
        return ParseCompare();
    }

    private Expr ParseCompare()
    {
        int start = _pos;
        var left = ParseOperand();
        while (Peek is { } t && IsCompareOp(t))
        {
            string op = t.Text;
            _pos++;
            var right = ParseOperand();
            left = new Compare(op, left, right) { Source = Span(start, _pos) };
        }
        return left;
    }

    private static bool IsCompareOp(Token t)
        => (t.Kind == TokenKind.Operator && CompareOps.Contains(t.Text)) || t.Is("Like") || t.Is("Is");

    private bool IsBoundary(int idx)
    {
        if (idx >= _tokens.Count) return true;
        var t = _tokens[idx];
        if (t.Kind == TokenKind.Operator && t.Text == ")") return true;
        if (IsCompareOp(t)) return true;
        return LogicalWords.Any(t.Is);
    }

    private Expr ParseOperand()
    {
        var t = Peek ?? throw new ExprParseException("İfade beklenmeden bitti");
        int start = _pos;

        if (t.Kind == TokenKind.Operator && t.Text == "(")
        {
            _pos++;
            var inner = ParseLevel(0);
            if (Peek is not { Text: ")" }) throw new ExprParseException("')' bekleniyordu");
            _pos++;
            if (IsBoundary(_pos)) return new Paren(inner) { Source = Span(start, _pos) };
            // (a + b) * 2 gibi: tamamı opak
            _pos = start;
            return ParseOpaque();
        }

        if (t.Kind == TokenKind.Identifier && IsBoundary(_pos + 1))
        {
            _pos++;
            if (t.Is("True")) return new BoolLit(true) { Source = t.Text };
            if (t.Is("False")) return new BoolLit(false) { Source = t.Text };
            return new Ident(t.Text) { Source = t.Text };
        }

        // Modul.Bayrak
        if (t.Kind == TokenKind.Identifier && _moduleNames != null && _moduleNames.Contains(t.Text)
            && _pos + 2 < _tokens.Count && _tokens[_pos + 1].Text == "." && _tokens[_pos + 2].Kind == TokenKind.Identifier
            && IsBoundary(_pos + 3) && _tokens[_pos + 1].Start == t.End && _tokens[_pos + 2].Start == _tokens[_pos + 1].End)
        {
            _pos += 3;
            return new Ident(_tokens[_pos - 1].Text) { Source = Span(start, _pos) };
        }

        return ParseOpaque();
    }

    private Expr ParseOpaque()
    {
        int start = _pos;
        int depth = 0;
        bool hasCall = false;
        while (_pos < _tokens.Count)
        {
            var t = _tokens[_pos];
            if (depth == 0 && IsBoundary(_pos)) break;
            if (t.Kind == TokenKind.Operator && t.Text == "(") depth++;
            else if (t.Kind == TokenKind.Operator && t.Text == ")") depth--;
            if (t.Kind == TokenKind.Identifier && _pos + 1 < _tokens.Count && _tokens[_pos + 1].Text == "(") hasCall = true;
            if (t.Kind == TokenKind.Operator && t.Text == ".") hasCall = true; // nesne özelliği/metodu: yan etki olabilir
            _pos++;
        }
        if (_pos == start) throw new ExprParseException($"İfade bekleniyordu: {Peek?.Text}");
        var text = Span(start, _pos);
        return new Opaque(text, hasCall) { Source = text };
    }
}

/// <summary>AST'yi VB koduna çevirir. Source'u dolu düğümler aynen yazılır.</summary>
public static class ExprPrinter
{
    private const int PrecAtom = 10, PrecCompare = 7, PrecNot = 6;

    public static int Precedence(Expr e) => e switch
    {
        Binary b => b.Op switch { LogicOp.Imp => 1, LogicOp.Eqv => 2, LogicOp.Xor => 3, LogicOp.Or => 4, _ => 5 },
        NotExpr => PrecNot,
        Compare => PrecCompare,
        _ => PrecAtom,
    };

    public static string Print(Expr e)
    {
        if (e.Source != null) return e.Source;
        return e switch
        {
            BoolLit b => b.Value ? "True" : "False",
            Ident i => i.Name,
            Opaque o => o.Text,
            Paren p => "(" + Print(p.Inner) + ")",
            NotExpr n => "Not " + Wrap(n.Operand, NeedsParenUnderNot(n.Operand)),
            Compare c => Wrap(c.Left, Precedence(c.Left) <= PrecCompare) + " " + c.Op + " " + Wrap(c.Right, Precedence(c.Right) <= PrecCompare),
            Binary b => Wrap(b.Left, Precedence(b.Left) < Precedence(b)) + " " + b.Op + " " + Wrap(b.Right, Precedence(b.Right) <= Precedence(b)),
            _ => throw new InvalidOperationException(e.GetType().Name),
        };
    }

    private static bool NeedsParenUnderNot(Expr operand) => operand switch
    {
        Binary => true,
        Compare => true, // okunabilirlik: Not (a = b)
        Opaque o => o.Text.Contains(' '),
        _ => false,
    };

    private static string Wrap(Expr e, bool paren) => paren && e is not Paren ? "(" + Print(e) + ")" : Print(e);
}
