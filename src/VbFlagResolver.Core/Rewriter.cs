namespace VbFlagResolver.Core;

public sealed class RewriteResult
{
    public required byte[] Output { get; init; }
    public required bool Changed { get; init; }
    public required FileReport Report { get; init; }
    /// <summary>Dosyada geçen bayrak isimleri (kullanım sayımı için).</summary>
    public required IReadOnlyCollection<string> FlagReferences { get; init; }
}

/// <summary>
/// Blok ağacını bayrak değerlerine göre işleyip kodu yeniden üretir.
/// Değişmeyen satırlar orijinal baytlarıyla yazılır.
/// </summary>
public sealed class Rewriter
{
    private readonly FlagTable _flags;
    private readonly ISet<string> _moduleNames;
    private readonly Simplifier _simplifier;

    private IReadOnlyList<PhysicalLine> _code = [];
    private FileReport _report = null!;
    private bool _changed;
    private readonly List<(string Name, int Line, string What)> _removedDecls = [];
    /// <summary>Mantıksal satır → yordam numarası (0: modül düzeyi) ve yordam başına isim geçiş sayıları.</summary>
    private readonly Dictionary<LogicalLine, int> _procOf = [];
    private readonly Dictionary<int, Dictionary<string, int>> _procNames = [];

    private readonly CondConstants _constants;
    private readonly bool _resolveDirectives;

    public Rewriter(FlagTable flags, ISet<string>? moduleNames = null, CondConstants? constants = null, bool resolveDirectives = true)
    {
        _flags = flags;
        _moduleNames = moduleNames ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _simplifier = new Simplifier(flags);
        _constants = constants ?? new CondConstants();
        _resolveDirectives = resolveDirectives;
    }

    /// <summary>Dosyayı dönüştürür. Beklenmeyen bir hata tüm çalışmayı durdurmaz: dosya değiştirilmeden kalır ve hatalı raporlanır.</summary>
    public RewriteResult Rewrite(VbSourceFile file, string? relativePath = null)
    {
        try
        {
            return RewriteCore(file, relativePath);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            var report = new FileReport { RelativePath = relativePath ?? Path.GetFileName(file.Path), Failed = true };
            report.Warnings.Add(new SourceNote(report.RelativePath, 0, $"Beklenmeyen hata, dosya değiştirilmedi: {ex.GetType().Name}: {ex.Message}"));
            return new RewriteResult { Output = file.OriginalBytes, Changed = false, Report = report, FlagReferences = new HashSet<string>() };
        }
    }

    private RewriteResult RewriteCore(VbSourceFile file, string? relativePath)
    {
        _code = file.CodeLines;
        _report = new FileReport { RelativePath = relativePath ?? Path.GetFileName(file.Path) };
        _changed = false;
        _removedDecls.Clear();

        var logical = file.GetLogicalLines();
        var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in logical)
        foreach (var t in l.Tokens)
            if (t.Kind == TokenKind.Identifier && _flags.IsFlag(t.Text))
                refs.Add(_flags.Get(t.Text)!.Name);

        // önce #If grupları çözülür; kalan satırlar normal If işlemine girer
        var dr = DirectiveResolver.Resolve(logical, _constants, _resolveDirectives, _report.RelativePath);
        if (dr.Error != null)
            _report.Warnings.Add(new SourceNote(_report.RelativePath, dr.ErrorLine, $"#If yapısı çözümlenemedi: {dr.Error}; #If satırları olduğu gibi bırakıldı"));
        _report.Warnings.AddRange(dr.Warnings);
        _report.Changes.AddRange(dr.Changes);
        _report.Directives = dr;
        if (dr.Changed) _changed = true;
        _directiveCommentOnly = dr.CommentOnly;
        IndexProcedures(dr.Kept);

        List<Node> tree;
        try
        {
            tree = BlockParser.Parse(dr.Kept);
        }
        catch (BlockParseException ex)
        {
            _report.Failed = true;
            _report.Changes.Clear();
            _report.Warnings.Add(new SourceNote(_report.RelativePath, ex.Line, $"Blok yapısı çözümlenemedi: {ex.Message}"));
            return new RewriteResult { Output = file.OriginalBytes, Changed = false, Report = _report, FlagReferences = refs };
        }

        var output = new List<OutputLine>();
        EmitNodes(tree, output);

        if (!_changed)
            return new RewriteResult { Output = file.OriginalBytes, Changed = false, Report = _report, FlagReferences = refs };

        CheckRemovedDeclarations(output);
        KeepEmptiedProcedures(logical, dr.Kept.ToHashSet(), output);
        _report.Changed = true;
        _report.RemovedLines = _code.Count - output.Count;

        var all = file.Lines.Take(file.HeaderLineCount).Select(OutputLine.From).Concat(output);
        return new RewriteResult { Output = file.Render(all), Changed = true, Report = _report, FlagReferences = refs };
    }

    // ---------------------------------------------------------------- yayınlama

    private void EmitNodes(List<Node> nodes, List<OutputLine> o)
    {
        foreach (var node in nodes)
        {
            if (node is IfBlock blk) EmitIf(blk, o);
            else EmitLine(((LineNode)node).Line, o);
        }
    }

    private IEnumerable<PhysicalLine> Physical(ClassifiedLine c) => _code.Skip(c.Line.Start).Take(c.Line.Count);

    private string LastEnding(ClassifiedLine c) => _code[c.Line.Start + c.Line.Count - 1].Ending;

    private void Verbatim(ClassifiedLine c, List<OutputLine> o)
    {
        if (c.Virtual) return;
        if (_directiveCommentOnly.TryGetValue(c.Line, out var indent) && Lexer.Comment(c.Tokens) is { } cm)
        {
            // çözülen #If satırının yalnızca yorumu kalır
            o.Add(new OutputLine { Text = indent + c.Line.Text[cm.Start..].TrimEnd(), Ending = LastEnding(c) });
            return;
        }
        o.AddRange(Physical(c).Select(OutputLine.From));
    }

    private IReadOnlyDictionary<LogicalLine, string> _directiveCommentOnly = new Dictionary<LogicalLine, string>();

    private void Warn(int line, string message, string? text = null)
        => _report.Warnings.Add(new SourceNote(_report.RelativePath, line, message, text));

    private void EmitLine(ClassifiedLine c, List<OutputLine> o)
    {
        if (c.Kind == LineKind.SingleIf)
        {
            EmitSingleIf(c, o);
            return;
        }
        var used = c.Tokens.Where(t => t.Kind == TokenKind.Identifier && _flags.TryGetKnown(t.Text, out _)).Select(t => t.Text).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (used.Count > 0)
            Warn(c.Line.LineNumber, $"Bayrak koşul dışında kullanılıyor, değiştirilmedi: {string.Join(", ", used)}", c.Line.Text.Trim());
        Verbatim(c, o);
    }

    private SimplifyResult? Evaluate(ClassifiedLine h)
    {
        var tokens = h.ConditionTokens;
        if (tokens.Count == 0) return null;
        try
        {
            var expr = ExprParser.Parse(tokens, h.Line.Text, _moduleNames);
            var r = _simplifier.Simplify(expr);
            foreach (var call in r.DroppedCalls)
                Warn(h.Line.LineNumber, $"Kısa devre yok: silinen kısımdaki '{call}' VB'de çalışırdı (yan etki olabilir)", h.Line.Text.Trim());
            return r;
        }
        catch (ExprParseException ex)
        {
            if (tokens.Any(t => t.Kind == TokenKind.Identifier && _flags.IsFlag(t.Text)))
                Warn(h.Line.LineNumber, $"Koşul çözümlenemedi ({ex.Message}); değiştirilmedi", h.Line.Text.Trim());
            return null;
        }
    }

    /// <summary>Kullanıcının dokunulmasın dediği bayraklar: koşulunda geçen If'e (bütün bloğuyla) dokunulmaz.</summary>
    public ISet<string> SkipFlags { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private bool HasSkipFlag(ClassifiedLine h)
        => SkipFlags.Count > 0 && h.ConditionTokens.Any(t => t.Kind == TokenKind.Identifier && SkipFlags.Contains(t.Text));

    private void EmitSingleIf(ClassifiedLine c, List<OutputLine> o)
    {
        if (HasSkipFlag(c)) { Verbatim(c, o); return; }
        var r = Evaluate(c);
        if (r is not { Changed: true })
        {
            Verbatim(c, o);
            return;
        }

        var tokens = c.Tokens;
        string text = c.Line.Text;
        int n = tokens.Count - (c.Comment != null ? 1 : 0);
        int restStart = c.ThenIndex + 1;
        while (restStart < n && tokens[restStart].Text == ":") restStart++; // "If c Then: x = 1"

        int elseIdx = -1;
        for (int i = restStart; i < n; i++)
        {
            if (tokens[i].Is("If"))
            {
                Warn(c.Line.LineNumber, "İç içe tek satırlık If desteklenmiyor; değiştirilmedi", text.Trim());
                Verbatim(c, o);
                return;
            }
            if (tokens[i].Is("Else") && elseIdx < 0) elseIdx = i;
        }

        var ifTok = tokens[c.KeywordIndex];
        string prefix = text[..ifTok.Start];
        int thenEnd = elseIdx >= 0 ? elseIdx : n;
        string thenText = thenEnd > restStart ? text[tokens[restStart].Start..tokens[thenEnd - 1].End] : "";
        string? elseText = elseIdx >= 0 && elseIdx + 1 < n ? text[tokens[elseIdx + 1].Start..tokens[n - 1].End] : null;
        string tail = c.Comment != null ? text[tokens[n - 1].End..] : "";
        string condSrc = text[c.ConditionTokens[0].Start..c.ConditionTokens[^1].End];

        string? newText;
        string desc;
        int keptFrom = r.Value == Tri.True ? restStart : elseIdx + 1;
        string? kept = r.Value == Tri.True ? thenText : elseText;
        switch (r.Value)
        {
            case Tri.True or Tri.False when !string.IsNullOrWhiteSpace(kept):
                newText = prefix + LiftedStatement(kept, tokens, keptFrom, n, prefix.Trim().Length == 0) + tail;
                desc = r.Value == Tri.True
                    ? $"If {condSrc} Then ... (tek satır) → True, Then kısmı kaldı"
                    : $"If {condSrc} Then ... (tek satır) → False, Else kısmı kaldı";
                break;
            case Tri.True or Tri.False:
                newText = prefix.Trim().Length == 0 ? null : TrimStatementSeparator(tokens, c.KeywordIndex, prefix) + tail;
                desc = $"If {condSrc} Then ... (tek satır) → {(r.Value == Tri.True ? "True, Then kısmı boş" : "False")}, silindi";
                break;
            default:
                var cond = c.ConditionTokens;
                newText = text[..cond[0].Start] + r.Text + text[cond[^1].End..];
                desc = $"If {condSrc} → If {r.Text} (tek satır)";
                break;
        }

        _changed = true;
        _report.Changes.Add(new ChangeRecord(c.Line.LineNumber, ChangeKind.SingleLineIf, desc));
        if (newText != null) EmitRewritten(c, newText, o);
    }

    private void EmitIf(IfBlock blk, List<OutputLine> o)
    {
        // dalların herhangi birinde dokunulmayacak bayrak varsa blok yapısı aynen kalır; gövdeler yine işlenir
        if (blk.Branches.Any(b => HasSkipFlag(b.Header)))
        {
            foreach (var b in blk.Branches)
            {
                Verbatim(b.Header, o);
                EmitNodes(b.Body, o);
            }
            Verbatim(blk.End!, o);
            return;
        }
        var removed = new List<Branch>();
        var kept = new List<(Branch B, Tri V, SimplifyResult? R)>();
        foreach (var b in blk.Branches)
        {
            if (b.Kind == LineKind.Else)
            {
                kept.Add((b, Tri.True, null));
                break;
            }
            var r = Evaluate(b.Header);
            var v = r?.Value ?? Tri.Unknown;
            if (v == Tri.False) { removed.Add(b); continue; }
            kept.Add((b, v, r));
            if (v == Tri.True) break;
        }
        removed.AddRange(blk.Branches.Skip(removed.Count + kept.Count));

        bool structural = removed.Count > 0 || kept.Any(k => k.R?.Changed == true);
        var first = blk.Branches[0].Header;
        if (structural)
        {
            var unclean = blk.Branches.Select(b => b.Header).Append(blk.End!).FirstOrDefault(h => !h.Clean);
            if (unclean != null)
            {
                Warn(unclean.Line.LineNumber, "Blok satırı etiket veya ':' ile ek ifade içeriyor; bu If bloğu değiştirilmedi", unclean.Line.Text.Trim());
                structural = false;
            }
            // silinecek dalda yarım kalan #If/#End If varsa silmek derlenmeyen kod üretir
            else if (removed.FirstOrDefault(b => !DirectivesBalanced(BranchLines(b))) is { } crossing)
            {
                Warn(crossing.Header.Line.LineNumber, "Blok çözülemeyen bir #If/#End If ile kesişiyor; bu If bloğu değiştirilmedi", crossing.Header.Line.Text.Trim());
                structural = false;
            }
            // silinecek dalda yordamın başka yerinden atlanan etiket veya kullanılan Dim/Const varsa silmek derlenmeyen kod üretir
            else if (removed.Select(NeededElsewhere).FirstOrDefault(x => x != null) is { } need)
            {
                Warn(need.Line, $"Silinecek dalda {need.What} '{need.Name}' yordamın başka yerinde kullanılıyor; silinse kod derlenmezdi, bu If bloğu değiştirilmedi");
                structural = false;
            }
        }

        if (!structural)
        {
            foreach (var b in blk.Branches)
            {
                Verbatim(b.Header, o);
                EmitNodes(b.Body, o);
            }
            Verbatim(blk.End!, o);
            return;
        }

        _changed = true;
        foreach (var b in removed) CollectRemoved(b);
        string condSrc = CondSource(first);

        if (kept.Count == 0)
        {
            _report.Changes.Add(new ChangeRecord(first.Line.LineNumber, ChangeKind.BlockRemoved, $"If {condSrc} → tüm blok silindi"));
            return;
        }

        if (kept[0].V == Tri.True)
        {
            var b = kept[0].B;
            string which = b.Kind switch { LineKind.BlockIf => "If dalı", LineKind.ElseIf => $"ElseIf {CondSource(b.Header)} dalı", _ => "Else dalı" };
            _report.Changes.Add(new ChangeRecord(first.Line.LineNumber, ChangeKind.BlockLifted, $"If {condSrc} → {which} yerine kondu"));
            var body = new List<OutputLine>();
            EmitNodes(b.Body, body);
            // kalkan If/ElseIf/Else/End If satırlarındaki yorumlar kaybolmasın: kendi satırlarında kalır
            HeaderComment(first, o);
            if (b.Header != first) HeaderComment(b.Header, o);
            int delta = IndentDelta(b);
            o.AddRange(Dedent(body, delta, Width(b.Header.Line.Indent) + delta));
            HeaderComment(blk.End!, o);
            return;
        }

        var parts = new List<string>();
        for (int i = 0; i < kept.Count; i++)
        {
            var (b, v, r) = kept[i];
            var newKind = i == 0 ? LineKind.BlockIf : v == Tri.True ? LineKind.Else : LineKind.ElseIf;
            if (newKind == b.Kind && r?.Changed != true)
            {
                Verbatim(b.Header, o);
            }
            else
            {
                var text = BuildHeader(b.Header, newKind, r);
                EmitRewritten(b.Header, text, o);
                parts.Add(text.Trim());
            }
            EmitNodes(b.Body, o);
        }
        Verbatim(blk.End!, o);
        string detail = removed.Count > 0 ? $"{removed.Count} dal silindi" : "koşul sadeleşti";
        _report.Changes.Add(new ChangeRecord(first.Line.LineNumber, ChangeKind.BlockSimplified,
            $"If {condSrc} → {detail}{(parts.Count > 0 ? ": " + string.Join(" / ", parts) : "")}"));
    }

    // ayrılmış deyim sözcükleri etiket olamaz ("Close:" etiket değildir); bunlara "Call" eklenmez (Call Close derlenmez)
    private static readonly string[] ReservedStatements =
    [
        "End", "Stop", "Return", "Resume", "Exit", "Loop", "Next", "Wend", "Else", "Rem", "Close", "Print", "Reset", "Do",
        "Error", "Get", "Put", "Open", "Width", "Line", "Input", "Write", "Lock", "Unlock", "Seek", "Name", "Erase", "Set",
        "Let", "Dim", "ReDim", "On", "GoTo", "GoSub", "With", "For", "While", "Select", "Case", "Call", "RaiseEvent",
        "Static", "Const", "Mid", "LSet", "RSet", "Date", "Time", "Option", "Private", "Public", "Global", "Event", "Implements",
    ];

    /// <summary>
    /// Tek satırlık If'ten kalan ifade satır başına gelince anlamı değişmesin: "100" (örtük GoTo) → "GoTo 100",
    /// "Init: x = 1" (Init çağrısı) → "Call Init: x = 1"; yoksa Init etikete dönüşür.
    /// </summary>
    private static string LiftedStatement(string kept, List<Token> tokens, int from, int to, bool atLineStart)
    {
        if (from >= to) return kept;
        if (tokens[from].Kind == TokenKind.Number) return "GoTo " + kept;
        if (atLineStart && from + 1 < to && tokens[from].Kind == TokenKind.Identifier && tokens[from + 1].Text == ":"
            && !ReservedStatements.Any(tokens[from].Is))
            return "Call " + kept;
        return kept;
    }

    /// <summary>"x = 1: " → "x = 1". Tek tanımlayıcı + ':' bir etikettir ("Hata:"), korunur.</summary>
    private static string TrimStatementSeparator(List<Token> tokens, int ifIndex, string prefix)
    {
        var p = prefix.TrimEnd();
        bool isLabel = ifIndex == 2 && tokens[0].Kind == TokenKind.Identifier || ifIndex == 1;
        if (!isLabel && p.EndsWith(':')) p = p[..^1].TrimEnd();
        return p;
    }

    private void HeaderComment(ClassifiedLine h, List<OutputLine> o)
    {
        if (h.Comment is not { } c) return;
        if (CommentContinuation(h) is var (first, rest))
        {
            o.Add(new OutputLine { Text = h.Line.Indent + first, Ending = PhysicalBefore(h, rest[0]).Ending });
            o.AddRange(rest.Select(OutputLine.From));
            return;
        }
        o.Add(new OutputLine { Text = h.Line.Indent + h.Line.Text[c.Start..].TrimEnd(), Ending = LastEnding(h) });
    }

    /// <summary>
    /// VB'de " _" ile biten yorum sonraki satıra devam eder; mantıksal satır bunları birleştirir. Yorum bir fiziksel
    /// satırda başlayıp sonrakilere taşıyorsa (yorumun ilk satırdaki kısmı, devam eden fiziksel satırlar), değilse null.
    /// </summary>
    private (string First, List<PhysicalLine> Tail)? CommentContinuation(ClassifiedLine c)
    {
        if (c.Line.Count < 2 || c.Comment == null) return null;
        var phys = Physical(c).ToList();
        for (int k = 0; k < phys.Count - 1; k++)
        {
            if (Lexer.Comment(Lexer.Tokenize(phys[k].Text)) is not { } cm) continue;
            return (phys[k].Text[cm.Start..].TrimEnd(), phys.Skip(k + 1).ToList());
        }
        return null;
    }

    private PhysicalLine PhysicalBefore(ClassifiedLine c, PhysicalLine p)
    {
        var phys = Physical(c).ToList();
        return phys[Math.Max(0, phys.IndexOf(p) - 1)];
    }

    /// <summary>
    /// Mantıksal satırdan yeniden üretilmiş metni yazar. Satır sonundaki yorum " _" ile alt satırlara devam ediyorsa
    /// birleştirilmiş yorum yerine yorumun ilk satırı ve devam satırları aynen yazılır.
    /// </summary>
    private void EmitRewritten(ClassifiedLine c, string newText, List<OutputLine> o)
    {
        if (c.Comment is { } cm && CommentContinuation(c) is var (first, rest))
        {
            string merged = c.Line.Text[cm.Start..].TrimEnd();
            string t = newText.TrimEnd();
            if (t.EndsWith(merged, StringComparison.Ordinal))
            {
                o.Add(new OutputLine { Text = t[..^merged.Length] + first, Ending = PhysicalBefore(c, rest[0]).Ending });
                o.AddRange(rest.Select(OutputLine.From));
                return;
            }
        }
        o.Add(new OutputLine { Text = newText, Ending = LastEnding(c) });
    }

    private static IEnumerable<ClassifiedLine> BranchLines(Branch b)
        => b.Body.SelectMany(BlockParser.AllLines).Prepend(b.Header);

    /// <summary>Satırlardaki #If/#Else/#End If kendi içinde dengeli mi?</summary>
    private bool DirectivesBalanced(IEnumerable<ClassifiedLine> lines)
    {
        int depth = 0;
        foreach (var l in lines)
        {
            var t = l.Tokens;
            // çözülmüş #If satırlarının yalnızca yorumu kalır; yapıya katılmaz
            if (t.Count == 0 || t[0].Kind != TokenKind.Directive || _directiveCommentOnly.ContainsKey(l.Line)) continue;
            var w = t[0].Text;
            if (w.Equals("#If", StringComparison.OrdinalIgnoreCase)) depth++;
            else if (w.Equals("#EndIf", StringComparison.OrdinalIgnoreCase) || w.Equals("#End", StringComparison.OrdinalIgnoreCase) && t.Count > 1 && t[1].Is("If"))
            {
                if (--depth < 0) return false;
            }
            else if ((w.Equals("#Else", StringComparison.OrdinalIgnoreCase) || w.Equals("#ElseIf", StringComparison.OrdinalIgnoreCase)) && depth == 0)
                return false;
        }
        return depth == 0;
    }

    private static string CondSource(ClassifiedLine h)
    {
        var t = h.ConditionTokens;
        return t.Count == 0 ? "" : h.Line.Text[t[0].Start..t[^1].End];
    }

    private static string BuildHeader(ClassifiedLine h, LineKind kind, SimplifyResult? r)
    {
        string text = h.Line.Text;
        string indent = h.Line.Indent;
        var then = h.Tokens[h.ThenIndex];
        if (kind == LineKind.Else)
            return indent + "Else" + text[then.End..];
        string cond = r is { Changed: true } ? r.Text : CondSource(h);
        string kw = kind == LineKind.BlockIf ? "If" : "ElseIf";
        if (kind == h.Kind)
        {
            // yalnızca koşul değişti: satırın geri kalanı aynen kalır
            var ct = h.ConditionTokens;
            return text[..ct[0].Start] + cond + text[ct[^1].End..];
        }
        return indent + kw + " " + cond + " " + text[then.Start..];
    }

    // ---------------------------------------------------------------- girinti

    /// <summary>Girinti genişliği (sekme 4 sütun, VB IDE varsayılanı).</summary>
    private static int Width(string indent)
    {
        int w = 0;
        foreach (char ch in indent) w = ch == '\t' ? (w / 4 + 1) * 4 : w + 1;
        return w;
    }

    /// <summary>Dal gövdesinin, başlık satırına göre fazladan girintisi (sütun).</summary>
    private static int IndentDelta(Branch b)
    {
        int header = Width(b.Header.Line.Indent);
        foreach (var node in b.Body)
        {
            var line = node is IfBlock blk ? blk.Branches[0].Header : ((LineNode)node).Line;
            var toks = line.Tokens;
            if (toks.Count == 0 || toks[0].Kind == TokenKind.Comment || toks[0].Kind == TokenKind.Number
                || toks[0].Kind == TokenKind.Directive || toks.Count > 1 && toks[0].Kind == TokenKind.Identifier && toks[1].Text == ":")
                continue;
            // ölçü, başlıktan derin ilk kod satırıdır; sonraki hatalı girintili satırlar (yarım seviye) ölçüyü bozmasın
            int width = Width(line.Line.Indent);
            if (width > header) return width - header;
        }
        // gövdede kod yoksa (yalnızca yorum) ölçü başlıktan derin ilk yorum satırıdır
        foreach (var node in b.Body)
        {
            if (node is not LineNode { Line: var l } || l.Tokens.Count == 0 || l.Tokens[0].Kind != TokenKind.Comment) continue;
            int width = Width(l.Line.Indent);
            if (width > header) return width - header;
        }
        return 0;
    }

    /// <summary>
    /// En az <paramref name="minWidth"/> sütun girintili satırlardan baştaki boşlukları tam <paramref name="delta"/> sütun
    /// kaldırır; daha az girintili satırlar (hatalı girinti, yorum) olduğu gibi kalır.
    /// </summary>
    public static List<OutputLine> Dedent(List<OutputLine> lines, int delta, int minWidth = 0)
    {
        if (delta <= 0) return lines;
        var result = new List<OutputLine>(lines.Count);
        foreach (var l in lines)
        {
            string t = l.Text;
            int lead = 0;
            while (lead < t.Length && (t[lead] == ' ' || t[lead] == '\t')) lead++;
            int w = 0, col = 0;
            if (Width(t[..lead]) >= Math.Max(minWidth, delta))
            {
                while (w < lead && col < delta)
                {
                    int next = t[w] == '\t' ? (col / 4 + 1) * 4 : col + 1;
                    if (next > delta) break;
                    col = next;
                    w++;
                }
                if (col != delta) w = 0; // sekme ortasına denk geliyor: dokunma
            }
            if (w == 0 || t.Trim().Length == 0)
            {
                result.Add(l);
                continue;
            }
            // baştaki boşluklar 1254'te tek bayttır; orijinal baytlar kesilerek korunur
            result.Add(new OutputLine { Text = t[w..], Ending = l.Ending, OriginalBytes = l.OriginalBytes?[w..] });
        }
        return result;
    }

    // ---------------------------------------------------------------- silinen kod kontrolleri

    private void CollectRemoved(Branch b)
    {
        _removedDecls.AddRange(Declarations(b));
        foreach (var line in b.Body.SelectMany(BlockParser.AllLines))
        {
            var toks = line.Tokens;
            int k = toks.Count > 0 && toks[0].Kind == TokenKind.Number ? 1 : toks.Count > 1 && toks[0].Kind == TokenKind.Identifier && toks[1].Text == ":" ? 2 : 0;
            if (k >= toks.Count) continue;
            if (Lexer.SplitStatements(toks.GetRange(k, toks.Count - k)).Any(s => s.Count > 1 && s[0].Is("On") && s[1].Is("Error")))
                Warn(line.Line.LineNumber, "Silinen blokta On Error ifadesi var; hata yakalama davranışı değişebilir", line.Line.Text.Trim());
        }
    }

    /// <summary>Daldaki etiketler ve Dim/Static/ReDim/Const bildirimleri.</summary>
    private static IEnumerable<(string Name, int Line, string What)> Declarations(Branch b)
    {
        foreach (var line in b.Body.SelectMany(BlockParser.AllLines))
        {
            var toks = line.Tokens;
            if (toks.Count == 0 || toks[0].Kind == TokenKind.Comment) continue;
            int lineNo = line.Line.LineNumber;
            int k = 0;
            if (toks[0].Kind == TokenKind.Number)
            {
                yield return (toks[0].Text, lineNo, "satır numarası etiketi");
                k = 1;
            }
            else if (toks.Count > 1 && toks[0].Kind == TokenKind.Identifier && toks[1].Text == ":" && !toks[0].Is("Else"))
            {
                yield return (toks[0].Text, lineNo, "etiket");
                k = 2;
            }
            if (k >= toks.Count) continue;
            foreach (var stmt in Lexer.SplitStatements(toks.GetRange(k, toks.Count - k)))
            {
                if (stmt.Count == 0 || !(stmt[0].Is("Dim") || stmt[0].Is("Static") || stmt[0].Is("ReDim") || stmt[0].Is("Const"))) continue;
                int s = stmt.Count > 1 && stmt[1].Is("Preserve") ? 2 : 1;
                foreach (var decl in FlagReader.SplitTopLevel(stmt, s, ","))
                    if (decl.Count > 0 && decl[0].Kind == TokenKind.Identifier)
                        yield return (decl[0].Text, lineNo, stmt[0].Text + " bildirimi");
            }
        }
    }

    /// <summary>
    /// Daldaki bir etiket/Dim/Const yordamın dal dışındaki bir satırında da geçiyor mu? (GoTo hedefi, değişken kullanımı)
    /// ReDim çoğunlukla başka yerde bildirilmiş diziyi boyutlandırır; bildirim sayılmaz.
    /// </summary>
    private (string Name, int Line, string What)? NeededElsewhere(Branch b)
    {
        if (!_procOf.TryGetValue(b.Header.Line, out int proc) || !_procNames.TryGetValue(proc, out var total)) return null;
        var inBranch = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in BranchLines(b))
            CountNames(l.Tokens, inBranch);
        foreach (var d in Declarations(b))
            if (!d.What.StartsWith("ReDim", StringComparison.OrdinalIgnoreCase)
                && total.GetValueOrDefault(d.Name) > inBranch.GetValueOrDefault(d.Name))
                return d;
        return null;
    }

    private static readonly string[] ProcScopes = ["Public", "Private", "Friend", "Static", "Global"];

    /// <summary>Boşalan yordama eklenen satır: VB IDE içi boş yordamı kaydederken siler, yorum satırı olanı silmez.</summary>
    public const string EmptiedProcedureComment = "' (VbFlagResolver: bayrağa bağlı kod kaldırıldı; VB boş yordamı silmesin diye bırakıldı)";

    /// <summary>Satır bir yordam başlığıysa "Sub X" / "Property Get X" gibi anahtarı, değilse null.</summary>
    private static string? ProcedureKey(List<Token> t)
    {
        int k = 0;
        while (k < t.Count && ProcScopes.Any(t[k].Is)) k++;
        if (k >= t.Count || !(t[k].Is("Sub") || t[k].Is("Function") || t[k].Is("Property"))) return null;
        int name = t[k].Is("Property") ? k + 2 : k + 1;
        if (name >= t.Count) return null;
        // tek satırda biten yordam ("Sub X(): End Sub") ayrıca ele alınmaz
        if (t.Any(x => x.Is("End")) && t.Count > name + 1) return null;
        return string.Join(" ", t.Skip(k).Take(name - k + 1).Select(x => x.Text.ToLowerInvariant()));
    }

    /// <summary>"Sub X(): y = 1" — başlık satırında ':' sonrası ifade de yordamın içeriğidir.</summary>
    private static bool HeaderHasStatement(List<Token> t)
    {
        var code = t.Where(x => x.Kind != TokenKind.Comment).ToList();
        int colon = code.FindIndex(x => x.Text == ":");
        return colon >= 0 && colon < code.Count - 1;
    }

    private static bool IsProcedureEnd(List<Token> t)
        => t.Count > 1 && t[0].Is("End") && (t[1].Is("Sub") || t[1].Is("Function") || t[1].Is("Property"));

    /// <summary>
    /// Orijinalde içi dolu olup temizlikten sonra hiç satırı kalmayan yordamlara yorum satırı ekler; yoksa VB IDE
    /// yordamı siler ve çağrıldığı yerler derlenmez.
    /// </summary>
    private void KeepEmptiedProcedures(IReadOnlyList<LogicalLine> original, IReadOnlyCollection<LogicalLine> compiled, List<OutputLine> output)
    {
        // orijinalde içinde en az bir satır (kod veya yorum) olan yordamlar; aynı adlılar sırayla sayılır.
        // Yalnızca derlenen (#If ile silinmeyen) başlık ve End satırları sayılır: iki #If dalında aynı adlı yordam olabilir.
        // İçerik ise #If ile silinen satırları da kapsar (yordam #If yüzünden boşalmış olabilir); #If satırlarının kendisi sayılmaz.
        var kept = compiled as ISet<LogicalLine> ?? compiled.ToHashSet();
        var hadContent = new Dictionary<string, Queue<(bool Content, int Line)>>();
        string? key = null;
        bool content = false;
        int headerLine = 0;
        foreach (var l in original)
        {
            if (key == null)
            {
                if (kept.Contains(l) && ProcedureKey(l.Tokens) is { } k)
                {
                    key = k;
                    headerLine = l.LineNumber;
                    content = HeaderHasStatement(l.Tokens);
                }
                continue;
            }
            if (IsProcedureEnd(l.Tokens))
            {
                if (!kept.Contains(l)) continue; // derlenmeyen daldaki başka bir yordamın sonu
                if (!hadContent.TryGetValue(key, out var q)) hadContent[key] = q = new();
                q.Enqueue((content, headerLine));
                key = null;
            }
            else if (l.Tokens.Count > 0 && l.Tokens[0].Kind != TokenKind.Directive && ProcedureKey(l.Tokens) == null) content = true;
        }

        for (int i = 0; i < output.Count; i++)
        {
            var toks = Lexer.Tokenize(output[i].Text);
            if (ProcedureKey(toks) is not { } k) continue;
            int start = i;
            // satır devamıyla süren başlık
            while (start + 1 < output.Count && output[start].Text.TrimEnd().EndsWith(" _")) start++;
            int j = start + 1;
            while (j < output.Count && output[j].Text.Trim().Length == 0) j++;
            if (j >= output.Count) break;
            bool empty = IsProcedureEnd(Lexer.Tokenize(output[j].Text)) && !HeaderHasStatement(toks);
            var (wasFull, line) = hadContent.TryGetValue(k, out var q) && q.Count > 0 ? q.Dequeue() : (false, 0);
            if (empty && wasFull)
            {
                string indent = output[i].Text[..(output[i].Text.Length - output[i].Text.TrimStart().Length)];
                output.Insert(start + 1, new OutputLine { Text = indent + "    " + EmptiedProcedureComment, Ending = output[start].Ending });
                j++;
                Warn(line, $"Yordamın içi tamamen silindi; VB IDE boş yordamı silmesin diye yorum satırı eklendi: {output[i].Text.Trim()}. " +
                           "Yordam artık bir şey yapmıyor; gerekmiyorsa çağrılarıyla birlikte elle silebilirsiniz.");
            }
            i = j;
        }
    }

    /// <summary>
    /// İsim geçişlerini sayar. Sayılar yalnızca satır numarası etiketi olarak geçtiğinde sayılır: satır başında (tanım)
    /// veya GoTo/GoSub/Resume/Then/Else ya da "On x GoTo a, b" listesinden sonra (kullanım); "y = 100" sayılmaz.
    /// </summary>
    private static void CountNames(List<Token> t, Dictionary<string, int> names)
    {
        bool inJumpList = false;
        for (int i = 0; i < t.Count; i++)
        {
            var tok = t[i];
            if (tok.Is("GoTo") || tok.Is("GoSub")) inJumpList = true;
            else if (tok.Text == ":" || tok.Kind == TokenKind.Comment) inJumpList = false;
            bool count = tok.Kind == TokenKind.Identifier
                || tok.Kind == TokenKind.Number && (i == 0 || inJumpList && (t[i - 1].Is("GoTo") || t[i - 1].Is("GoSub") || t[i - 1].Text == ",")
                    || t[i - 1].Is("Resume") || t[i - 1].Is("Then") || t[i - 1].Is("Else"));
            if (count) names[tok.Text] = names.GetValueOrDefault(tok.Text) + 1;
        }
    }

    /// <summary>Her mantıksal satırın hangi yordamda olduğunu ve yordam başına isim geçişlerini çıkarır.</summary>
    private void IndexProcedures(IReadOnlyList<LogicalLine> lines)
    {
        _procOf.Clear();
        _procNames.Clear();
        int proc = 0, next = 0;
        foreach (var l in lines)
        {
            var t = l.Tokens;
            int k = 0;
            while (k < t.Count && ProcScopes.Any(t[k].Is)) k++;
            if (k < t.Count && (t[k].Is("Sub") || t[k].Is("Function") || t[k].Is("Property"))) proc = ++next;
            _procOf[l] = proc;
            if (!_procNames.TryGetValue(proc, out var names)) _procNames[proc] = names = new(StringComparer.OrdinalIgnoreCase);
            CountNames(t, names);
            if (t.Count > 1 && t[0].Is("End") && (t[1].Is("Sub") || t[1].Is("Function") || t[1].Is("Property"))) proc = 0;
        }
    }

    private void CheckRemovedDeclarations(List<OutputLine> output)
    {
        if (_removedDecls.Count == 0) return;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // kalan kodda Dim/Private/... ile bildirilenler
        foreach (var l in output)
        {
            var toks = Lexer.Tokenize(l.Text);
            foreach (var t in toks)
                if (t.Kind is TokenKind.Identifier or TokenKind.Number)
                    names.Add(t.Text);
            if (toks.Count > 1 && (toks[0].Is("Dim") || toks[0].Is("Private") || toks[0].Is("Public") || toks[0].Is("Static") || toks[0].Is("Global")))
                foreach (var t in toks.Skip(1))
                    if (t.Kind == TokenKind.Identifier) declared.Add(t.Text);
        }
        foreach (var (name, line, what) in _removedDecls)
            // ReDim, başka yerde bildirilmiş diziyi yeniden boyutlandırıyorsa bildirim değildir
            if (names.Contains(name) && !(what.StartsWith("ReDim", StringComparison.OrdinalIgnoreCase) && declared.Contains(name)))
                Warn(line, $"Silinen blokta {what} '{name}' var ve dosyanın başka yerinde kullanılıyor");
    }
}
