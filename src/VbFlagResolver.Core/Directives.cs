using System.Globalization;

namespace VbFlagResolver.Core;

/// <summary>
/// Koşullu derleme sabitleri (#Const / .vbp CondComp / kullanıcı). İsimler harf duyarsız.
/// Değer null ise sabit tanımlı ama değeri bilinmiyor demektir.
/// </summary>
public sealed class CondConstants
{
    private readonly Dictionary<string, (DValue? Value, string Source)> _values = new(StringComparer.OrdinalIgnoreCase);

    public void Set(string name, DValue? value, string source) => _values[name] = (value, source);

    /// <summary>false: tanımsız. true ve value null: tanımlı ama bilinmiyor.</summary>
    public bool TryGet(string name, out DValue? value)
    {
        if (_values.TryGetValue(name, out var v)) { value = v.Value; return true; }
        value = null;
        return false;
    }

    public bool Contains(string name) => _values.ContainsKey(name);

    /// <summary>
    /// true: hiçbir yerde tanımlanmamış sabit VB'deki gibi 0 (False) sayılır. false: bilinmiyor sayılır ve
    /// o #If grubu olduğu gibi bırakılır (sabit listesi eksik olabilir diye temkinli davranış).
    /// </summary>
    public bool UndefinedIsZero { get; set; }

    public CondConstants Clone()
    {
        var c = new CondConstants { UndefinedIsZero = UndefinedIsZero };
        foreach (var (k, v) in _values) c._values[k] = v;
        return c;
    }

    public IEnumerable<(string Name, DValue? Value, string Source)> All
        => _values.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => (kv.Key, kv.Value.Value, kv.Value.Source));

    public int Count => _values.Count;

    /// <summary>"A = 1 : B = 0" (vbp biçimi) veya "A=1, B=0" (kullanıcı biçimi).</summary>
    public static List<(string Name, DValue? Value)> ParseList(string spec)
    {
        var list = new List<(string, DValue?)>();
        foreach (var part in spec.Split([':', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            string name = part[..eq].Trim();
            string expr = part[(eq + 1)..].Trim();
            if (name.Length == 0 || expr.Length == 0) continue;
            var tokens = Lexer.Tokenize(expr);
            var v = DirectiveEvaluator.Evaluate(tokens, new CondConstants(), out _);
            list.Add((name, v.Known ? v : null));
        }
        return list;
    }

    /// <summary>
    /// Proje sabitleri: kaynak klasördeki .vbp dosyalarının CondComp satırı, üzerine kullanıcının verdikleri.
    /// Farklı .vbp'ler aynı sabite farklı değer veriyorsa sabit bilinmiyor sayılır.
    /// </summary>
    public static CondConstants FromProject(string? sourceDir, string? overrides, List<SourceNote> notes, int codePage = 1254)
    {
        var c = new CondConstants();
        if (sourceDir != null && Directory.Exists(sourceDir))
        {
            var enc = VbSourceFile.GetEncoding(codePage);
            foreach (var vbp in Directory.EnumerateFiles(sourceDir, "*.vbp", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                string rel = Path.GetRelativePath(sourceDir, vbp);
                foreach (var raw in File.ReadAllLines(vbp, enc))
                {
                    var line = raw.Trim();
                    if (!line.StartsWith("CondComp", StringComparison.OrdinalIgnoreCase)) continue;
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    var spec = line[(eq + 1)..].Trim().Trim('"');
                    foreach (var (name, value) in ParseList(spec))
                    {
                        if (c._values.TryGetValue(name, out var old) && !Equals(old.Value, value))
                        {
                            c.Set(name, null, $"{old.Source}, {rel} (farklı değerler)");
                            notes.Add(new SourceNote(rel, 0, $"Derleme sabiti {name} .vbp dosyalarında farklı değerlerle tanımlı; bilinmiyor sayıldı"));
                        }
                        else
                        {
                            c.Set(name, value, $"{rel} CondComp");
                        }
                    }
                }
            }
        }
        if (!string.IsNullOrWhiteSpace(overrides))
        {
            foreach (var bad in InvalidEntries(overrides))
                notes.Add(new SourceNote("--define", 0, $"Geçersiz derleme sabiti '{bad}' yok sayıldı (AD=değer biçiminde olmalı, ör. DEBUG_MODE=1)"));
            foreach (var (name, value) in ParseList(overrides))
            {
                c.Set(name, value, "kullanıcı (--define)");
                if (value == null) notes.Add(new SourceNote("--define", 0, $"Derleme sabiti {name}: değer hesaplanamadı, bilinmiyor sayıldı"));
            }
        }
        return c;
    }

    /// <summary>"AD=değer" biçiminde olmayan parçalar (kullanıcı girdisini doğrulamak için).</summary>
    public static List<string> InvalidEntries(string spec)
        => spec.Split([':', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.IndexOf('=') is var eq && (eq <= 0 || p[(eq + 1)..].Trim().Length == 0 || !IsName(p[..eq].Trim())))
            .ToList();

    private static bool IsName(string s) => s.Length > 0 && char.IsLetter(s[0]) && s.All(ch => char.IsLetterOrDigit(ch) || ch == '_');
}

/// <summary>Koşullu derleme ifadesinin değeri: sayı veya metin; Known=false ise bilinmiyor.</summary>
public readonly record struct DValue(bool Known, double Number, string? Text = null)
{
    public static readonly DValue Unknown = new(false, 0);
    public static DValue Num(double d) => new(true, d);
    public static DValue Bool(bool b) => new(true, b ? -1 : 0);

    /// <summary>#If için doğruluk: sıfırdan farklı sayı. Metin sayıya çevrilemezse bilinmiyor.</summary>
    public bool? Truth()
    {
        if (!Known) return null;
        if (Text == null) return Number != 0;
        return double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d != 0 : null;
    }

    public override string ToString()
        => !Known ? "?" : Text != null ? $"\"{Text}\"" : Number.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// #If / #ElseIf / #Const ifadelerini değerlendirir. VB önceliği (yüksekten düşüğe):
/// ^, tekli -, * /, \, Mod, + -, &amp;, karşılaştırma, Not, And, Or, Xor, Eqv, Imp.
/// Mantık işleçleri VB'deki gibi tamsayılar üzerinde bit düzeyindedir.
/// </summary>
public sealed class DirectiveEvaluator
{
    private readonly List<Token> _t;
    private readonly CondConstants _consts;
    private readonly List<string> _undefined = [];
    private int _pos;

    private DirectiveEvaluator(List<Token> tokens, CondConstants consts)
    {
        _t = tokens.Where(x => x.Kind != TokenKind.Comment).ToList();
        _consts = consts;
    }

    /// <summary>Değerlendirir; tanımsız sabitler <paramref name="undefined"/>'a eklenir ve bilinmiyor sayılır.</summary>
    public static DValue Evaluate(List<Token> tokens, CondConstants consts, out List<string> undefined)
    {
        var e = new DirectiveEvaluator(tokens, consts);
        undefined = e._undefined;
        if (e._t.Count == 0) return DValue.Unknown;
        try
        {
            var v = e.Level(0);
            return e._pos == e._t.Count ? v : DValue.Unknown;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            return DValue.Unknown;
        }
    }

    private Token? Peek => _pos < _t.Count ? _t[_pos] : null;
    private bool Accept(string word)
    {
        if (Peek is { } t && t.Is(word)) { _pos++; return true; }
        return false;
    }

    private static readonly string[] Logical = ["Imp", "Eqv", "Xor", "Or", "And"];

    private DValue Level(int i)
    {
        if (i == Logical.Length) return NotLevel();
        var left = Level(i + 1);
        while (Accept(Logical[i]))
        {
            var right = Level(i + 1);
            left = LogicOp(Logical[i], left, right);
        }
        return left;
    }

    private static DValue LogicOp(string op, DValue a, DValue b)
    {
        // VB'de And/Or kısa devre yapmaz ama sonuç bilinen işlenenle kesinleşebilir: 0 And x = 0, -1 Or x = -1
        if (op == "And" && (IsZero(a) || IsZero(b))) return DValue.Num(0);
        if (op == "Or" && (IsAllOnes(a) || IsAllOnes(b))) return DValue.Num(-1);
        if (!a.Known || !b.Known) return DValue.Unknown;
        long x = ToLong(a), y = ToLong(b);
        return DValue.Num(op switch
        {
            "And" => x & y,
            "Or" => x | y,
            "Xor" => x ^ y,
            "Eqv" => ~(x ^ y),
            _ => ~x | y, // Imp
        });
    }

    private static bool IsZero(DValue v) => v.Known && v.Text == null && v.Number == 0;
    private static bool IsAllOnes(DValue v) => v.Known && v.Text == null && v.Number == -1;

    private static long ToLong(DValue v)
    {
        if (v.Text != null)
            return double.TryParse(v.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (long)Math.Round(d) : throw new FormatException();
        return (long)Math.Round(v.Number, MidpointRounding.ToEven);
    }

    private DValue NotLevel()
    {
        if (Accept("Not"))
        {
            var v = NotLevel();
            return v.Known ? DValue.Num(~ToLong(v)) : DValue.Unknown;
        }
        return Comparison();
    }

    private static readonly string[] CompareOps = ["=", "<>", "<", ">", "<=", ">=", "=<", "=>"];

    private DValue Comparison()
    {
        var left = Concat();
        while (Peek is { Kind: TokenKind.Operator } t && CompareOps.Contains(t.Text))
        {
            _pos++;
            var right = Concat();
            if (!left.Known || !right.Known) { left = DValue.Unknown; continue; }
            int cmp = left.Text != null && right.Text != null
                ? string.Compare(left.Text, right.Text, StringComparison.Ordinal)
                : AsNumber(left).CompareTo(AsNumber(right));
            left = DValue.Bool(t.Text switch
            {
                "=" => cmp == 0,
                "<>" => cmp != 0,
                "<" => cmp < 0,
                ">" => cmp > 0,
                "<=" or "=<" => cmp <= 0,
                _ => cmp >= 0,
            });
        }
        return left;
    }

    private static double AsNumber(DValue v)
        => v.Text == null ? v.Number
            : double.TryParse(v.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : throw new FormatException();

    private DValue Concat()
    {
        var left = Additive();
        while (Peek is { Kind: TokenKind.Operator, Text: "&" })
        {
            _pos++;
            var right = Additive();
            left = left.Known && right.Known ? new DValue(true, 0, Str(left) + Str(right)) : DValue.Unknown;
        }
        return left;
    }

    private static string Str(DValue v) => v.Text ?? v.Number.ToString(CultureInfo.InvariantCulture);

    private DValue Additive()
    {
        var left = ModLevel();
        while (Peek is { Kind: TokenKind.Operator } t && t.Text is "+" or "-")
        {
            _pos++;
            var right = ModLevel();
            left = Arith(left, right, (a, b) => t.Text == "+" ? a + b : a - b);
        }
        return left;
    }

    private DValue ModLevel()
    {
        var left = IntDiv();
        while (Accept("Mod"))
        {
            var right = IntDiv();
            left = Arith(left, right, (a, b) => b == 0 ? throw new FormatException() : Math.Round(a) % Math.Round(b));
        }
        return left;
    }

    private DValue IntDiv()
    {
        var left = MulDiv();
        while (Peek is { Kind: TokenKind.Operator, Text: "\\" })
        {
            _pos++;
            var right = MulDiv();
            left = Arith(left, right, (a, b) => Math.Round(b) == 0 ? throw new FormatException() : Math.Truncate(Math.Round(a) / Math.Round(b)));
        }
        return left;
    }

    private DValue MulDiv()
    {
        var left = Unary();
        while (Peek is { Kind: TokenKind.Operator } t && t.Text is "*" or "/")
        {
            _pos++;
            var right = Unary();
            left = Arith(left, right, (a, b) => t.Text == "*" ? a * b : b == 0 ? throw new FormatException() : a / b);
        }
        return left;
    }

    private DValue Unary()
    {
        if (Peek is { Kind: TokenKind.Operator, Text: "-" })
        {
            _pos++;
            var v = Unary();
            return v.Known ? DValue.Num(-AsNumber(v)) : DValue.Unknown;
        }
        if (Peek is { Kind: TokenKind.Operator, Text: "+" }) { _pos++; return Unary(); }
        return Power();
    }

    private DValue Power()
    {
        var left = Primary();
        while (Peek is { Kind: TokenKind.Operator, Text: "^" })
        {
            _pos++;
            var right = Primary();
            left = Arith(left, right, Math.Pow);
        }
        return left;
    }

    private static DValue Arith(DValue a, DValue b, Func<double, double, double> f)
        => a.Known && b.Known ? DValue.Num(f(AsNumber(a), AsNumber(b))) : DValue.Unknown;

    private DValue Primary()
    {
        var t = Peek ?? throw new FormatException();
        _pos++;
        switch (t.Kind)
        {
            case TokenKind.Number:
                return DValue.Num(ParseNumber(t.Text));
            case TokenKind.String:
                return new DValue(true, 0, t.Text[1..^1].Replace("\"\"", "\""));
            case TokenKind.Operator when t.Text == "(":
            {
                var v = Level(0);
                if (!Accept(")")) throw new FormatException();
                return v;
            }
            case TokenKind.Identifier:
                if (t.Is("True")) return DValue.Bool(true);
                if (t.Is("False")) return DValue.Bool(false);
                if (t.Is("Empty")) return DValue.Num(0);
                if (_consts.TryGet(t.Text, out var value)) return value ?? DValue.Unknown;
                // VB5/VB6'nın yerleşik derleme sabitleri (kullanıcı tanımlarsa onunki geçerli)
                if (t.Is("Win32")) return DValue.Bool(true);
                if (t.Is("Win16")) return DValue.Bool(false);
                _undefined.Add(t.Text);
                return _consts.UndefinedIsZero ? DValue.Num(0) : DValue.Unknown; // VB: tanımsız sabit Empty → 0
            default:
                throw new FormatException();
        }
    }

    internal static double ParseNumber(string text)
    {
        var s = text.TrimEnd('%', '&', '!', '#', '@');
        bool isLong = text.EndsWith('&');
        int radix = s.StartsWith("&H", StringComparison.OrdinalIgnoreCase) ? 16 : s.StartsWith("&O", StringComparison.OrdinalIgnoreCase) ? 8 : 0;
        if (radix == 0) return double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        // VB: &H/&O sabitleri işaretli Integer (16 bit) veya Long (32 bit) olarak yorumlanır: &HFFFF = -1, &HFFFF& = 65535
        ulong u = Convert.ToUInt64(s[2..], radix); // 64 bitten büyükse OverflowException → bilinmiyor
        if (!isLong && u <= 0xFFFF) return (short)(ushort)u;
        if (u <= 0xFFFFFFFF) return (int)(uint)u;
        throw new OverflowException("VB'de 32 bitten büyük &H/&O sabiti derlenmez");
    }
}

/// <summary>#If çözümlemesinin sonucu.</summary>
public sealed class DirectiveResult
{
    /// <summary>Çözümlemeden sonra kalan mantıksal satırlar (kaynak sırasıyla).</summary>
    public List<LogicalLine> Kept { get; } = [];
    /// <summary>Çözülemeyen bir #If grubunun içinde kalan satırlar (koşullu derlenir).</summary>
    public HashSet<LogicalLine> Conditional { get; } = [];
    /// <summary>Kalan satırı kapsayan çözülemeyen #If koşulu (açıklama için).</summary>
    public Dictionary<LogicalLine, string> ConditionText { get; } = [];
    /// <summary>
    /// Çözülüp silinen #If/#ElseIf/#Else/#End If satırlarından yalnızca yorumu kalacak olanlar → yorumun girintisi.
    /// Bu satırlar Kept içindedir; yazılırken satırın yerine yalnızca yorum yazılır.
    /// </summary>
    public Dictionary<LogicalLine, string> CommentOnly { get; } = [];
    public List<ChangeRecord> Changes { get; } = [];
    public List<SourceNote> Warnings { get; } = [];
    /// <summary>#If yapısı bozuksa açıklama; o zaman Kept orijinal satırların tamamıdır.</summary>
    public string? Error { get; set; }
    public int ErrorLine { get; set; }
    public bool Changed => Changes.Count > 0;
}

/// <summary>
/// #If / #ElseIf / #Else / #End If gruplarını sabitlere göre çözer: derlenecek dalın satırları kalır,
/// diğer dallar ve yönerge satırları kalkar. Koşulu bilinmeyen grup olduğu gibi korunur (içi yine işlenir).
/// </summary>
public static class DirectiveResolver
{
    private abstract class DNode;
    private sealed class DLine(LogicalLine line) : DNode { public LogicalLine Line { get; } = line; }
    private sealed class DBranch
    {
        public required LogicalLine Header { get; init; }
        /// <summary>null: #Else.</summary>
        public List<Token>? Condition { get; init; }
        public List<DNode> Body { get; } = [];
    }
    private sealed class DGroup : DNode
    {
        public List<DBranch> Branches { get; } = [];
        public LogicalLine? End { get; set; }
    }

    private enum DirKind { None, If, ElseIf, Else, EndIf, Const, Other }

    private static DirKind Kind(LogicalLine l, out List<Token> cond)
    {
        cond = [];
        var t = l.Tokens;
        if (t.Count == 0 || t[0].Kind != TokenKind.Directive) return DirKind.None;
        var w = t[0].Text;
        int then = t.FindIndex(x => x.Is("Then"));
        if (w.Equals("#If", StringComparison.OrdinalIgnoreCase) || w.Equals("#ElseIf", StringComparison.OrdinalIgnoreCase))
        {
            int end = then > 0 ? then : t.Count - (Lexer.Comment(t) != null ? 1 : 0);
            cond = t.GetRange(1, Math.Max(0, end - 1));
            return w.Equals("#If", StringComparison.OrdinalIgnoreCase) ? DirKind.If : DirKind.ElseIf;
        }
        if (w.Equals("#Else", StringComparison.OrdinalIgnoreCase)) return DirKind.Else;
        if (w.Equals("#EndIf", StringComparison.OrdinalIgnoreCase)) return DirKind.EndIf;
        if (w.Equals("#End", StringComparison.OrdinalIgnoreCase) && t.Count > 1 && t[1].Is("If")) return DirKind.EndIf;
        if (w.Equals("#Const", StringComparison.OrdinalIgnoreCase)) return DirKind.Const;
        return DirKind.Other;
    }

    public static bool IsDirectiveLine(LogicalLine l) => l.Tokens.Count > 0 && l.Tokens[0].Kind == TokenKind.Directive;

    /// <param name="resolve">false: hiçbir grup çözülmez; yalnızca hangi satırların koşullu olduğu belirlenir.</param>
    public static DirectiveResult Resolve(IReadOnlyList<LogicalLine> lines, CondConstants constants, bool resolve, string fileName)
    {
        var result = new DirectiveResult();
        List<DNode> tree;
        try
        {
            tree = Parse(lines);
        }
        catch (BlockParseException ex)
        {
            result.Error = ex.Message;
            result.ErrorLine = ex.Line;
            result.Kept.AddRange(lines);
            return result;
        }
        Walk(tree, constants.Clone(), resolve, conditional: null, result, fileName);
        return result;
    }

    private static List<DNode> Parse(IReadOnlyList<LogicalLine> lines)
    {
        var root = new List<DNode>();
        var stack = new Stack<DGroup>();
        List<DNode> Current() => stack.Count == 0 ? root : stack.Peek().Branches[^1].Body;
        foreach (var l in lines)
        {
            switch (Kind(l, out var cond))
            {
                case DirKind.If:
                {
                    var g = new DGroup();
                    g.Branches.Add(new DBranch { Header = l, Condition = cond });
                    Current().Add(g);
                    stack.Push(g);
                    break;
                }
                case DirKind.ElseIf:
                case DirKind.Else:
                {
                    if (stack.Count == 0) throw new BlockParseException("Eşleşmeyen #Else/#ElseIf", l.LineNumber);
                    var g = stack.Peek();
                    if (g.Branches[^1].Condition == null) throw new BlockParseException("#Else'den sonra #ElseIf/#Else", l.LineNumber);
                    g.Branches.Add(new DBranch { Header = l, Condition = Kind(l, out _) == DirKind.Else ? null : cond });
                    break;
                }
                case DirKind.EndIf:
                    if (stack.Count == 0) throw new BlockParseException("Eşleşmeyen #End If", l.LineNumber);
                    stack.Pop().End = l;
                    break;
                default:
                    Current().Add(new DLine(l));
                    break;
            }
        }
        if (stack.Count > 0) throw new BlockParseException("#End If eksik", stack.Peek().Branches[0].Header.LineNumber);
        return root;
    }

    /// <param name="conditional">null: koşulsuz derlenen bölge; aksi halde kapsayan çözülemeyen koşulun metni.</param>
    private static void Walk(List<DNode> nodes, CondConstants consts, bool resolve, string? conditional, DirectiveResult r, string fileName)
    {
        foreach (var node in nodes)
        {
            if (node is DLine dl)
            {
                if (Kind(dl.Line, out _) == DirKind.Const) ApplyConst(dl.Line, consts, conditional, r, fileName);
                Keep(dl.Line, conditional, r);
                continue;
            }

            var g = (DGroup)node;
            string condText = CondText(g.Branches[0]);
            DBranch? chosen = null;
            bool unresolved = !resolve;
            var undefinedAll = new List<string>();
            if (resolve)
            {
                foreach (var b in g.Branches)
                {
                    if (b.Condition == null) { chosen = b; break; }
                    var v = DirectiveEvaluator.Evaluate(b.Condition, consts, out var undefined);
                    undefinedAll.AddRange(undefined);
                    var truth = v.Truth();
                    if (truth == null) { unresolved = true; break; }
                    if (truth == true) { chosen = b; break; }
                }
            }

            if (unresolved)
            {
                if (resolve)
                    r.Warnings.Add(new SourceNote(fileName, g.Branches[0].Header.LineNumber,
                        $"#If {condText} çözülemedi" +
                        (undefinedAll.Count > 0 ? $" (tanımsız/bilinmeyen sabit: {string.Join(", ", undefinedAll.Distinct(StringComparer.OrdinalIgnoreCase))})" : "") +
                        "; grup olduğu gibi bırakıldı", g.Branches[0].Header.Text.Trim()));
                // grup korunur; her dal ayrı (kopya) sabitlerle işlenir, dallarda #Const ile değişenler sonra bilinmiyor olur
                var changedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var b in g.Branches)
                {
                    Keep(b.Header, conditional, r);
                    var copy = consts.Clone();
                    Walk(b.Body, copy, resolve, conditional ?? condText, r, fileName);
                    foreach (var (name, _, _) in copy.All)
                        if (!consts.TryGet(name, out var before) || !copy.TryGet(name, out var after) || !Equals(before, after))
                            changedNames.Add(name);
                }
                if (g.End != null) Keep(g.End, conditional, r);
                foreach (var n in changedNames) consts.Set(n, null, "koşullu #Const");
                continue;
            }

            string what = chosen == null ? "hiçbir dal derlenmiyor, grup silindi"
                : chosen == g.Branches[0] ? "#If dalı kaldı"
                : chosen.Condition == null ? "#Else dalı kaldı"
                : $"#ElseIf {CondText(chosen)} dalı kaldı";
            if (undefinedAll.Count > 0)
                what += $" (tanımsız sabit 0 sayıldı: {string.Join(", ", undefinedAll.Distinct(StringComparer.OrdinalIgnoreCase))})";
            r.Changes.Add(new ChangeRecord(g.Branches[0].Header.LineNumber, ChangeKind.DirectiveResolved, $"#If {condText} → {what}"));
            if (chosen != null)
            {
                // silinen #If, kalan dalın başlığı ve #End If satırlarındaki yorumlar kendi satırlarında kalır (If bloklarındaki gibi)
                string indent = chosen.Body.OfType<DLine>().Select(d => d.Line).FirstOrDefault(l => l.Tokens.Count > 0)?.Indent ?? g.Branches[0].Header.Indent;
                void KeepComment(LogicalLine? l)
                {
                    if (l == null || Lexer.Comment(l.Tokens) == null || r.CommentOnly.ContainsKey(l)) return;
                    r.CommentOnly[l] = indent;
                    Keep(l, conditional, r);
                }
                KeepComment(g.Branches[0].Header);
                KeepComment(chosen.Header);
                Walk(chosen.Body, consts, resolve, conditional, r, fileName);
                KeepComment(g.End);
            }
        }
    }

    private static void Keep(LogicalLine l, string? conditional, DirectiveResult r)
    {
        r.Kept.Add(l);
        if (conditional != null)
        {
            r.Conditional.Add(l);
            r.ConditionText[l] = conditional;
        }
    }

    private static string CondText(DBranch b)
    {
        if (b.Condition == null || b.Condition.Count == 0) return "";
        var text = b.Header.Text;
        return text[b.Condition[0].Start..b.Condition[^1].End];
    }

    /// <summary>#Const AD = ifade: koşulsuz bölgede değer atanır; koşullu bölgede sabit bilinmiyor olur.</summary>
    private static void ApplyConst(LogicalLine l, CondConstants consts, string? conditional, DirectiveResult r, string fileName)
    {
        var t = l.Tokens.Where(x => x.Kind != TokenKind.Comment).ToList();
        if (t.Count < 4 || t[1].Kind != TokenKind.Identifier || t[2].Text != "=") return;
        string name = t[1].Text;
        if (conditional != null)
        {
            consts.Set(name, null, $"{fileName}:{l.LineNumber} #Const (koşullu)");
            return;
        }
        var v = DirectiveEvaluator.Evaluate(t.GetRange(3, t.Count - 3), consts, out _);
        consts.Set(name, v.Known ? v : null, $"{fileName}:{l.LineNumber} #Const");
    }
}
