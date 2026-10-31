namespace VbFlagResolver.Core;

public enum FlagKind { Const, Variable }

public sealed class FlagInfo
{
    public required string Name { get; init; }
    public required FlagKind Kind { get; init; }
    /// <summary>null = Bilinmiyor.</summary>
    public bool? Value { get; set; }
    public required int Line { get; init; }
    /// <summary>Neden Bilinmiyor sayıldığı (atama, koşullu ilk değer, ...).</summary>
    public string? UnknownReason { get; set; }
    /// <summary>Değişken bayrak için: InitFlags benzeri yordamda atama yapıldı mı?</summary>
    internal bool Assigned { get; set; }
}

/// <summary>Bayrak tablosu. İsimler büyük/küçük harf duyarsızdır (VB kuralı).</summary>
public sealed class FlagTable
{
    private readonly Dictionary<string, FlagInfo> _flags = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<FlagInfo> All => _flags.Values;
    public int Count => _flags.Count;

    public bool IsFlag(string name) => _flags.ContainsKey(name);

    public FlagInfo? Get(string name) => _flags.GetValueOrDefault(name);

    public bool TryGetKnown(string name, out bool value)
    {
        if (_flags.TryGetValue(name, out var f) && f.Value is bool v)
        {
            value = v;
            return true;
        }
        value = false;
        return false;
    }

    public bool Add(FlagInfo flag) => _flags.TryAdd(flag.Name, flag);

    public void MarkUnknown(string name, string reason)
    {
        if (!_flags.TryGetValue(name, out var f)) return;
        f.Value = null;
        f.UnknownReason = f.UnknownReason == null ? reason : f.UnknownReason + "; " + reason;
    }

    /// <summary>Testler için: isim=değer çiftlerinden tablo.</summary>
    public static FlagTable FromValues(params (string name, bool? value)[] values)
    {
        var t = new FlagTable();
        int line = 1;
        foreach (var (n, v) in values)
            t.Add(new FlagInfo { Name = n, Kind = FlagKind.Const, Value = v, Line = line++, UnknownReason = v == null ? "test" : null });
        return t;
    }
}

public enum FlagReadMode
{
    /// <summary>Sabitler ve Boolean değişkenler (+ ilk değer yordamları).</summary>
    Auto,
    ConstOnly,
    VariablesOnly,
}

public sealed class FlagReaderOptions
{
    public FlagReadMode Mode { get; init; } = FlagReadMode.Auto;
    /// <summary>Değişken bayraklara değer atayan yordamlar (ör. InitFlags). null/boş = modüldeki tüm yordamlar.</summary>
    public IReadOnlyCollection<string>? InitProcedures { get; init; }
    /// <summary>Hiç atanmamış Boolean değişken bayrağı False (VB varsayılanı) say; false ise Bilinmiyor.</summary>
    public bool UnassignedVariableIsFalse { get; init; } = true;
    public int CodePage { get; init; } = 1254;
}

public sealed record SourceNote(string File, int Line, string Message, string? Text = null);

public sealed class FlagReadResult
{
    public required FlagTable Flags { get; init; }
    public required string ModuleName { get; init; }
    public List<SourceNote> Unrecognized { get; } = [];
    public List<SourceNote> Notes { get; } = [];
}

/// <summary>
/// Geliştirme bayrakları modülünü okur. Desteklenen biçimler:
/// [Public|Private|Global] Const X [As Boolean] = ifade (virgülle birden çok, satır devamı dahil),
/// [Public|Global|Dim|Private] X As Boolean + yordam içinde X = True/False.
/// </summary>
public static class FlagReader
{
    private static readonly string[] Scopes = ["Public", "Private", "Global", "Dim", "Static", "Friend"];
    private static readonly string[] BlockOpen = ["If", "Select", "For", "Do", "While", "With"];

    public static FlagReadResult Read(string path, FlagReaderOptions? options = null)
    {
        options ??= new FlagReaderOptions();
        return Read(VbSourceFile.Load(path, options.CodePage), options);
    }

    public static FlagReadResult Read(VbSourceFile file, FlagReaderOptions? options = null)
    {
        options ??= new FlagReaderOptions();
        var table = new FlagTable();
        var result = new FlagReadResult { Flags = table, ModuleName = file.ModuleName };
        string fileName = Path.GetFileName(file.Path);
        var initProcs = options.InitProcedures is { Count: > 0 }
            ? new HashSet<string>(options.InitProcedures, StringComparer.OrdinalIgnoreCase)
            : null;

        string? currentProc = null;
        int blockDepth = 0;
        var moduleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { file.ModuleName };

        foreach (var line in file.GetLogicalLines())
        {
            var tokens = line.Tokens;
            int lineNo = line.LineNumber;
            if (tokens.Count == 0 || tokens[0].Kind == TokenKind.Comment) continue;

            foreach (var stmt in Lexer.SplitStatements(tokens))
            {
                if (stmt.Count == 0) continue;
                void Unrecognized(string why) =>
                    result.Unrecognized.Add(new SourceNote(fileName, lineNo, why, line.Text.Trim()));

                var first = stmt[0];

                if (first.Is("Option") || first.Is("Attribute") || first.Kind == TokenKind.Directive
                    || first.Text.StartsWith("Def", StringComparison.OrdinalIgnoreCase) && first.Text.Length == 6)
                    continue;

                // yordam başı / sonu
                int k = 0;
                while (k < stmt.Count && (Scopes.Any(stmt[k].Is) || stmt[k].Is("Static"))) k++;
                if (k < stmt.Count && (stmt[k].Is("Sub") || stmt[k].Is("Function") || stmt[k].Is("Property")))
                {
                    int nameIdx = stmt[k].Is("Property") ? k + 2 : k + 1;
                    currentProc = nameIdx < stmt.Count ? stmt[nameIdx].Text : "?";
                    blockDepth = 0;
                    continue;
                }
                if (first.Is("End") && stmt.Count > 1 && (stmt[1].Is("Sub") || stmt[1].Is("Function") || stmt[1].Is("Property")))
                {
                    currentProc = null;
                    continue;
                }

                if (currentProc != null)
                {
                    ReadProcedureStatement(stmt, line.Text, currentProc, initProcs, ref blockDepth, table, options, moduleNames,
                        fileName, lineNo, result, Unrecognized);
                    continue;
                }

                // modül düzeyi bildirimler
                int s = 0;
                if (s < stmt.Count && Scopes.Any(stmt[s].Is)) s++;
                bool isConst = s < stmt.Count && stmt[s].Is("Const");
                if (isConst) s++;
                if (s == 0 && !isConst)
                {
                    Unrecognized("Tanınmayan modül düzeyi satır");
                    continue;
                }
                if (s < stmt.Count && (stmt[s].Is("Declare") || stmt[s].Is("Type") || stmt[s].Is("Enum") || stmt[s].Is("Event")))
                {
                    Unrecognized("Bayrak değil (Declare/Type/Enum/Event)");
                    continue;
                }

                foreach (var decl in SplitTopLevel(stmt, s, ","))
                {
                    if (isConst)
                    {
                        if (options.Mode == FlagReadMode.VariablesOnly) { Unrecognized("Sabit atlandı (yalnızca değişken modu)"); continue; }
                        ReadConst(decl, line.Text, table, moduleNames, fileName, lineNo, result, Unrecognized);
                    }
                    else
                    {
                        if (options.Mode == FlagReadMode.ConstOnly) { Unrecognized("Değişken atlandı (yalnızca sabit modu)"); continue; }
                        ReadVariable(decl, table, lineNo, Unrecognized);
                    }
                }
            }
        }

        foreach (var f in table.All.Where(f => f.Kind == FlagKind.Variable && !f.Assigned && f.Value == null && f.UnknownReason == null))
        {
            if (options.UnassignedVariableIsFalse)
            {
                f.Value = false;
                result.Notes.Add(new SourceNote(fileName, f.Line, $"{f.Name}: hiç atanmamış, VB varsayılanı False kabul edildi"));
            }
            else
            {
                table.MarkUnknown(f.Name, "hiç atanmamış");
            }
        }
        return result;
    }

    private static void ReadProcedureStatement(List<Token> stmt, string text, string proc, HashSet<string>? initProcs,
        ref int blockDepth, FlagTable table, FlagReaderOptions options, ISet<string> moduleNames,
        string fileName, int lineNo, FlagReadResult result, Action<string> unrecognized)
    {
        var first = stmt[0];
        // blok derinliği (koşullu atamaları ayırt etmek için)
        if (first.Is("End") || first.Is("Next") || first.Is("Loop") || first.Is("Wend"))
        {
            blockDepth = Math.Max(0, blockDepth - 1);
            return;
        }
        if (first.Is("Exit") || first.Is("Else") || first.Is("ElseIf") || first.Is("Case")) return;
        if (BlockOpen.Any(first.Is))
        {
            bool singleLineIf = first.Is("If") && IsSingleLineIf(stmt);
            if (!singleLineIf) blockDepth++;
            unrecognized(singleLineIf ? "Tek satırlık If (ilk değer yordamında desteklenmez)" : "Blok başlangıcı (içindeki atamalar koşullu sayılır)");
            return;
        }

        int s = first.Is("Let") ? 1 : 0;
        if (stmt.Count > s + 2 && stmt[s].Kind == TokenKind.Identifier && stmt[s + 1].Text == "=")
        {
            string name = stmt[s].Text;
            var flag = table.Get(name);
            if (flag == null) { unrecognized("Bayrak olmayan atama"); return; }
            if (flag.Kind == FlagKind.Const) { unrecognized("Sabite atama (derlenmez)"); return; }
            if (options.Mode == FlagReadMode.ConstOnly) { unrecognized("Değişken ataması atlandı (yalnızca sabit modu)"); return; }

            if (initProcs != null && !initProcs.Contains(proc))
            {
                table.MarkUnknown(flag.Name, $"{fileName}:{lineNo} {proc} içinde atanıyor (ilk değer yordamı değil)");
                flag.Assigned = true;
                return;
            }
            if (blockDepth > 0)
            {
                table.MarkUnknown(flag.Name, $"{fileName}:{lineNo} koşullu/döngü içinde atanıyor");
                flag.Assigned = true;
                return;
            }

            var exprTokens = stmt.GetRange(s + 2, stmt.Count - s - 2);
            bool? value = Evaluate(exprTokens, text, table, moduleNames);
            if (value == null)
            {
                table.MarkUnknown(flag.Name, $"{fileName}:{lineNo} değer hesaplanamadı");
            }
            else if (flag.Assigned && flag.Value != value)
            {
                table.MarkUnknown(flag.Name, $"{fileName}:{lineNo} farklı değerlerle birden çok kez atanıyor");
            }
            else if (flag.UnknownReason == null)
            {
                flag.Value = value;
            }
            flag.Assigned = true;
            return;
        }
        unrecognized("Tanınmayan yordam satırı");
    }

    internal static bool IsSingleLineIf(List<Token> stmt)
    {
        int then = stmt.FindIndex(t => t.Is("Then"));
        return then >= 0 && then < stmt.Count - 1 && stmt[then + 1].Kind != TokenKind.Comment;
    }

    private static void ReadConst(List<Token> decl, string text, FlagTable table, ISet<string> moduleNames,
        string fileName, int lineNo, FlagReadResult result, Action<string> unrecognized)
    {
        // Name [As Type] = expr
        int eq = decl.FindIndex(t => t.Text == "=");
        if (decl.Count == 0 || decl[0].Kind != TokenKind.Identifier || eq < 1 || eq == decl.Count - 1)
        {
            unrecognized("Sabit tanımı çözümlenemedi");
            return;
        }
        string name = decl[0].Text;
        string? type = eq >= 3 && decl[1].Is("As") ? decl[2].Text : null;
        if (type != null && !type.Equals("Boolean", StringComparison.OrdinalIgnoreCase))
        {
            unrecognized($"Boolean olmayan sabit: {name} As {type}");
            return;
        }
        var exprTokens = decl.GetRange(eq + 1, decl.Count - eq - 1);
        bool? value = Evaluate(exprTokens, text, table, moduleNames);
        if (value == null && type != null && exprTokens.Count == 1 && exprTokens[0].Kind == TokenKind.Number
            && double.TryParse(exprTokens[0].Text.TrimEnd('%', '&', '!', '#', '@'), out var num))
        {
            value = num != 0; // As Boolean = 1 → True
        }
        if (value == null)
        {
            if (type == null && !LooksBoolean(exprTokens, table))
            {
                unrecognized($"Boolean olmayan sabit: {name}");
                return;
            }
            var info = new FlagInfo { Name = name, Kind = FlagKind.Const, Value = null, Line = lineNo, UnknownReason = "değeri hesaplanamadı" };
            if (!table.Add(info)) result.Notes.Add(new SourceNote(fileName, lineNo, $"{name} birden çok kez tanımlanmış"));
            return;
        }
        if (!table.Add(new FlagInfo { Name = name, Kind = FlagKind.Const, Value = value, Line = lineNo }))
            result.Notes.Add(new SourceNote(fileName, lineNo, $"{name} birden çok kez tanımlanmış; ilk tanım kullanıldı"));
    }

    private static bool LooksBoolean(List<Token> tokens, FlagTable table)
        => tokens.Any(t => t.Is("True") || t.Is("False") || (t.Kind == TokenKind.Identifier && table.IsFlag(t.Text)));

    private static void ReadVariable(List<Token> decl, FlagTable table, int lineNo, Action<string> unrecognized)
    {
        int i = 0;
        if (i < decl.Count && decl[i].Is("WithEvents")) { unrecognized("WithEvents bildirimi"); return; }
        if (decl.Count == 0 || decl[0].Kind != TokenKind.Identifier) { unrecognized("Değişken bildirimi çözümlenemedi"); return; }
        string name = decl[0].Text;
        bool isBool = decl.Count == 3 && decl[1].Is("As") && decl[2].Is("Boolean");
        if (!isBool)
        {
            unrecognized($"Boolean olmayan değişken: {name}");
            return;
        }
        table.Add(new FlagInfo { Name = name, Kind = FlagKind.Variable, Value = null, Line = lineNo });
    }

    private static bool? Evaluate(List<Token> tokens, string text, FlagTable table, ISet<string> moduleNames)
    {
        try
        {
            var expr = ExprParser.Parse(tokens, text, moduleNames);
            if (expr is BoolLit b) return b.Value;
            var r = new Simplifier(table).Simplify(expr);
            return r.Value switch { Tri.True => true, Tri.False => false, _ => EvaluateLiteralOnly(expr) };
        }
        catch (ExprParseException)
        {
            return null;
        }
    }

    // "Not False", "(True)" gibi bayraksız ifadeler
    private static bool? EvaluateLiteralOnly(Expr e) => e switch
    {
        BoolLit b => b.Value,
        Paren p => EvaluateLiteralOnly(p.Inner),
        NotExpr n => EvaluateLiteralOnly(n.Operand) is bool v ? !v : null,
        _ => null,
    };

    internal static IEnumerable<List<Token>> SplitTopLevel(List<Token> tokens, int start, string sep)
    {
        var cur = new List<Token>();
        int depth = 0;
        for (int i = start; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Text == "(") depth++;
            else if (t.Text == ")") depth--;
            if (depth == 0 && t.Kind == TokenKind.Operator && t.Text == sep)
            {
                yield return cur;
                cur = [];
                continue;
            }
            cur.Add(t);
        }
        if (cur.Count > 0) yield return cur;
    }
}
