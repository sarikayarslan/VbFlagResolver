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

    public Rewriter(FlagTable flags, ISet<string>? moduleNames = null)
    {
        _flags = flags;
        _moduleNames = moduleNames ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _simplifier = new Simplifier(flags);
    }

    public RewriteResult Rewrite(VbSourceFile file, string? relativePath = null)
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

        List<Node> tree;
        try
        {
            tree = BlockParser.Parse(logical);
        }
        catch (BlockParseException ex)
        {
            _report.Failed = true;
            _report.Warnings.Add(new SourceNote(_report.RelativePath, ex.Line, $"Blok yapısı çözümlenemedi: {ex.Message}"));
            return new RewriteResult { Output = file.OriginalBytes, Changed = false, Report = _report, FlagReferences = refs };
        }

        var output = new List<OutputLine>();
        EmitNodes(tree, output);

        if (!_changed)
            return new RewriteResult { Output = file.OriginalBytes, Changed = false, Report = _report, FlagReferences = refs };

        CheckRemovedDeclarations(output);
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

    private void Verbatim(ClassifiedLine c, List<OutputLine> o) => o.AddRange(Physical(c).Select(OutputLine.From));

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

    private void EmitSingleIf(ClassifiedLine c, List<OutputLine> o)
    {
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
        switch (r.Value)
        {
            case Tri.True:
                newText = prefix + thenText + tail;
                desc = $"If {condSrc} Then ... (tek satır) → True, Then kısmı kaldı";
                break;
            case Tri.False when elseText != null:
                newText = prefix + elseText + tail;
                desc = $"If {condSrc} Then ... (tek satır) → False, Else kısmı kaldı";
                break;
            case Tri.False:
                newText = prefix.Trim().Length == 0 ? null : TrimStatementSeparator(tokens, c.KeywordIndex, prefix) + tail;
                desc = $"If {condSrc} Then ... (tek satır) → False, silindi";
                break;
            default:
                var cond = c.ConditionTokens;
                newText = text[..cond[0].Start] + r.Text + text[cond[^1].End..];
                desc = $"If {condSrc} → If {r.Text} (tek satır)";
                break;
        }

        _changed = true;
        _report.Changes.Add(new ChangeRecord(c.Line.LineNumber, ChangeKind.SingleLineIf, desc));
        if (newText != null) o.Add(new OutputLine { Text = newText, Ending = LastEnding(c) });
    }

    private void EmitIf(IfBlock blk, List<OutputLine> o)
    {
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
            o.AddRange(Dedent(body, IndentDelta(b)));
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
                o.Add(new OutputLine { Text = text, Ending = LastEnding(b.Header) });
                parts.Add(text.Trim());
            }
            EmitNodes(b.Body, o);
        }
        Verbatim(blk.End!, o);
        string detail = removed.Count > 0 ? $"{removed.Count} dal silindi" : "koşul sadeleşti";
        _report.Changes.Add(new ChangeRecord(first.Line.LineNumber, ChangeKind.BlockSimplified,
            $"If {condSrc} → {detail}{(parts.Count > 0 ? ": " + string.Join(" / ", parts) : "")}"));
    }

    /// <summary>"x = 1: " → "x = 1". Tek tanımlayıcı + ':' bir etikettir ("Hata:"), korunur.</summary>
    private static string TrimStatementSeparator(List<Token> tokens, int ifIndex, string prefix)
    {
        var p = prefix.TrimEnd();
        bool isLabel = ifIndex == 2 && tokens[0].Kind == TokenKind.Identifier || ifIndex == 1;
        if (!isLabel && p.EndsWith(':')) p = p[..^1].TrimEnd();
        return p;
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

    /// <summary>Dal gövdesinin, başlık satırına göre fazladan girintisi.</summary>
    private static int IndentDelta(Branch b)
    {
        int header = b.Header.Line.Indent.Length;
        int min = int.MaxValue;
        foreach (var node in b.Body)
        {
            var line = node is IfBlock blk ? blk.Branches[0].Header : ((LineNode)node).Line;
            var toks = line.Tokens;
            if (toks.Count == 0 || toks[0].Kind == TokenKind.Comment || toks[0].Kind == TokenKind.Number
                || toks[0].Kind == TokenKind.Directive || toks.Count > 1 && toks[0].Kind == TokenKind.Identifier && toks[1].Text == ":")
                continue;
            min = Math.Min(min, line.Line.Indent.Length);
        }
        return min == int.MaxValue ? 0 : Math.Max(0, min - header);
    }

    public static List<OutputLine> Dedent(List<OutputLine> lines, int delta)
    {
        if (delta <= 0) return lines;
        var result = new List<OutputLine>(lines.Count);
        foreach (var l in lines)
        {
            string t = l.Text;
            int w = 0;
            while (w < t.Length && w < delta && (t[w] == ' ' || t[w] == '\t')) w++;
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
        foreach (var line in b.Body.SelectMany(BlockParser.AllLines))
        {
            var toks = line.Tokens;
            if (toks.Count == 0 || toks[0].Kind == TokenKind.Comment) continue;
            int lineNo = line.Line.LineNumber;
            int k = 0;
            if (toks[0].Kind == TokenKind.Number)
            {
                _removedDecls.Add((toks[0].Text, lineNo, "satır numarası etiketi"));
                k = 1;
            }
            else if (toks.Count > 1 && toks[0].Kind == TokenKind.Identifier && toks[1].Text == ":" && !toks[0].Is("Else"))
            {
                _removedDecls.Add((toks[0].Text, lineNo, "etiket"));
                k = 2;
            }
            if (k >= toks.Count) continue;
            foreach (var stmt in Lexer.SplitStatements(toks.GetRange(k, toks.Count - k)))
            {
                if (stmt.Count == 0) continue;
                if (stmt[0].Is("Dim") || stmt[0].Is("Static") || stmt[0].Is("ReDim"))
                {
                    int s = stmt.Count > 1 && stmt[1].Is("Preserve") ? 2 : 1;
                    foreach (var decl in FlagReader.SplitTopLevel(stmt, s, ","))
                        if (decl.Count > 0 && decl[0].Kind == TokenKind.Identifier)
                            _removedDecls.Add((decl[0].Text, lineNo, stmt[0].Text + " bildirimi"));
                }
                else if (stmt[0].Is("On") && stmt.Count > 1 && stmt[1].Is("Error"))
                {
                    Warn(lineNo, "Silinen blokta On Error ifadesi var; hata yakalama davranışı değişebilir", line.Line.Text.Trim());
                }
            }
        }
    }

    private void CheckRemovedDeclarations(List<OutputLine> output)
    {
        if (_removedDecls.Count == 0) return;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in output)
        foreach (var t in Lexer.Tokenize(l.Text))
            if (t.Kind is TokenKind.Identifier or TokenKind.Number)
                names.Add(t.Text);
        foreach (var (name, line, what) in _removedDecls)
            if (names.Contains(name))
                Warn(line, $"Silinen blokta {what} '{name}' var ve dosyanın başka yerinde kullanılıyor");
    }
}
