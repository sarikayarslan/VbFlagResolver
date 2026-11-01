namespace VbFlagResolver.Core;

public sealed class ProcessOptions
{
    public required string FlagsPath { get; init; }
    public required string SourceDir { get; init; }
    /// <summary>Kuru çalıştırmada null olabilir.</summary>
    public string? OutputDir { get; init; }
    public bool DryRun { get; init; }
    public FlagReaderOptions FlagOptions { get; init; } = new();
    public IReadOnlyCollection<string> Extensions { get; init; } = [".frm", ".bas", ".cls", ".ctl", ".dob", ".pag"];
    /// <summary>Kaynak olmayan dosyaları (.frx, .vbp, ...) da çıktıya kopyala.</summary>
    public bool CopyOtherFiles { get; init; } = true;
    public int CodePage { get; init; } = 1254;
    /// <summary>Kullanıcının verdiği derleme sabitleri ("DEBUG_MODE=0, X=1"); .vbp CondComp değerlerini ezer.</summary>
    public string? Defines { get; init; }
    /// <summary>#If gruplarını derleme sabitlerine göre çöz.</summary>
    public bool ResolveDirectives { get; init; } = true;
}

/// <summary>Bayrakları okur, tüm kaynakları tarar, dönüştürür ve (kuru çalıştırma değilse) çıktıyı yazar.</summary>
public static class ProjectProcessor
{
    public static Report Run(ProcessOptions o, Action<string>? log = null)
    {
        var src = Path.GetFullPath(o.SourceDir);
        var flagsPath = Path.GetFullPath(o.FlagsPath);
        if (!Directory.Exists(src)) throw new DirectoryNotFoundException($"Kaynak klasör bulunamadı: {src}");
        if (!File.Exists(flagsPath)) throw new FileNotFoundException($"Bayrak dosyası bulunamadı: {flagsPath}");

        string? outDir = o.OutputDir == null ? null : Path.GetFullPath(o.OutputDir);
        var guard = new WriteGuard(src, flagsPath);
        if (!o.DryRun)
        {
            if (outDir == null) throw new ArgumentException("--out gerekli (veya --dry-run kullanın)");
            guard.CheckOutputDir(outDir);
        }

        var report = new Report { FlagsFile = flagsPath, SourceDir = src, OutputDir = outDir, DryRun = o.DryRun };

        // 0) koşullu derleme sabitleri: .vbp CondComp + kullanıcı
        var constNotes = new List<SourceNote>();
        var consts = CondConstants.FromProject(src, o.Defines, constNotes, o.CodePage);
        report.Constants = consts;
        report.ResolveDirectives = o.ResolveDirectives;
        report.FlagNotes.AddRange(constNotes);

        // 1) bayraklar
        var flagOptions = new FlagReaderOptions
        {
            Mode = o.FlagOptions.Mode,
            InitProcedures = o.FlagOptions.InitProcedures,
            UnassignedVariableIsFalse = o.FlagOptions.UnassignedVariableIsFalse,
            CodePage = o.CodePage,
            Constants = consts,
            ResolveDirectives = o.ResolveDirectives,
        };
        var flagResult = FlagReader.Read(flagsPath, flagOptions);
        report.Flags = flagResult.Flags;
        report.FlagNotes.AddRange(flagResult.Notes);
        report.UnrecognizedFlagLines.AddRange(flagResult.Unrecognized);
        log?.Invoke($"{flagResult.Flags.Count} bayrak okundu.");

        // 2) dosyalar
        var exts = new HashSet<string>(o.Extensions, StringComparer.OrdinalIgnoreCase);
        var allFiles = Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)
            .Where(f => outDir == null || !IsUnder(f, outDir))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var sources = new List<(string Rel, VbSourceFile File)>();
        var others = new List<string>();
        foreach (var f in allFiles)
        {
            if (exts.Contains(Path.GetExtension(f)) && !SamePath(f, flagsPath))
                sources.Add((Path.GetRelativePath(src, f), VbSourceFile.Load(f, o.CodePage)));
            else
                others.Add(f);
        }

        var moduleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { flagResult.ModuleName };

        // 3) bayrağa atama / gölgeleme taraması; bayrak yordamlarının çağrıldığı yerler
        report.FlagProcedures.AddRange(flagResult.Procedures);
        var procNames = new HashSet<string>(flagResult.Procedures.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var (rel, file) in sources)
        {
            // yalnızca derlenen satırlar: derlenmeyen #If dalındaki atama bayrağı Bilinmiyor yapmamalı
            var active = DirectiveResolver.Resolve(file.GetLogicalLines(), consts, o.ResolveDirectives, rel).Kept;
            ScanAssignments(file, rel, flagResult.Flags, moduleNames, report, active);
            if (procNames.Count > 0) report.ProcedureCalls.AddRange(ScanProcedureCalls(file, rel, procNames, moduleNames, active));
        }
        // çalıştırıldığı varsayılan ama projede hiç çağrılmayan bayrak yordamları
        foreach (var p in flagResult.Procedures.Where(p => p.IsRoot && p.AssignsFlags))
            if (!report.ProcedureCalls.Any(c => c.Procedure.Equals(p.Name, StringComparison.OrdinalIgnoreCase)))
                report.FlagNotes.Add(new SourceNote(Path.GetFileName(flagsPath), p.Line,
                    $"{p.Name} çalıştırılmış sayıldı ama projede çağrıldığı yer bulunamadı; hiç çalışmıyorsa atadığı bayraklar " +
                    "False (VB varsayılanı) kalır. Bu durumda 'Çağrı sırası'na (--init-proc) yalnızca çalışan yordamları yazın"));

        // 4) dönüşüm
        var outputs = new List<(string Rel, byte[] Bytes)>();
        var rewriter = new Rewriter(flagResult.Flags, moduleNames, consts, o.ResolveDirectives);
        foreach (var (rel, file) in sources)
        {
            var r = rewriter.Rewrite(file, rel);
            r.Report.OriginalBytes = file.OriginalBytes;
            r.Report.OutputBytes = r.Output;
            report.Files.Add(r.Report);
            foreach (var name in r.FlagReferences)
                report.FlagUsage[name] = report.FlagUsage.GetValueOrDefault(name) + 1;
            outputs.Add((rel, r.Output));
            if (r.Changed) log?.Invoke($"  {rel}: {r.Report.Changes.Count} değişiklik");
        }

        // 5) yazma
        foreach (var f in others) report.CopiedFiles.Add(Path.GetRelativePath(src, f));
        if (!o.DryRun)
        {
            foreach (var (rel, bytes) in outputs) Write(guard, outDir!, rel, bytes);
            // bayrak dosyası kaynak klasördeyse "others" içindedir ve aynen kopyalanır
            if (o.CopyOtherFiles)
                foreach (var f in others) Write(guard, outDir!, Path.GetRelativePath(src, f), File.ReadAllBytes(f));
        }
        return report;
    }

    private static void Write(WriteGuard guard, string outDir, string rel, byte[] bytes)
    {
        var target = Path.GetFullPath(Path.Combine(outDir, rel));
        guard.CheckFile(target); // yoldaki var olan junction/symlink'ler çözülür; olmayan klasörler bağlantı olamaz
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllBytes(target, bytes);
    }

    private static bool SamePath(string a, string b)
        => string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>path, dir'in içinde mi (aynısı dahil)?</summary>
    internal static bool IsUnder(string path, string dir)
    {
        var p = Path.GetFullPath(path).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        var d = Path.GetFullPath(dir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return p.StartsWith(d, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly string[] DeclWords = ["Dim", "Static", "Private", "Public", "Global", "Const", "ReDim"];

    /// <summary>
    /// Bayrağa atama (FlagX = ...) veya aynı isimde yerel bildirim varsa bayrağı Bilinmiyor yapar ve raporlar.
    /// </summary>
    public static void ScanAssignments(VbSourceFile file, string rel, FlagTable flags, ISet<string> moduleNames, Report report,
        IReadOnlyList<LogicalLine>? lines = null)
    {
        var privateReported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static bool IsPrivate(FlagInfo f) => f.Scope == null ? f.Kind == FlagKind.Const
            : f.Scope.Equals("Private", StringComparison.OrdinalIgnoreCase) || f.Scope.Equals("Dim", StringComparison.OrdinalIgnoreCase);

        void Shadow(Token t, LogicalLine line, string what, bool otherType = true)
        {
            if (t.Kind != TokenKind.Identifier || !flags.IsFlag(t.Text)) return;
            var flag = flags.Get(t.Text)!;
            var name = flag.Name;
            if (otherType) flag.Shadowed = true;
            flags.MarkUnknown(name, $"{rel}:{line.LineNumber} {what}");
            report.FlagAssignments.Add(new SourceNote(rel, line.LineNumber, $"{name}: {what}", line.Text.Trim()));
        }

        foreach (var line in lines ?? file.GetLogicalLines())
        {
            var toks = line.Tokens;
            int n = toks.Count - (Lexer.Comment(toks) != null ? 1 : 0);
            if (n == 0) continue;

            // yordam başı: aynı isimde parametre yordam içinde bayrağı gölgeler
            int h = 0;
            while (h < n && (DeclWords.Any(toks[h].Is) || toks[h].Is("Friend"))) h++;
            if (h < n && (toks[h].Is("Sub") || toks[h].Is("Function") || toks[h].Is("Property")))
            {
                int open = toks.FindIndex(h, n - h, t => t.Text == "(");
                if (open >= 0)
                {
                    int close = open, depth = 0;
                    for (; close < n; close++)
                    {
                        if (toks[close].Text == "(") depth++;
                        else if (toks[close].Text == ")" && --depth == 0) break;
                    }
                    foreach (var p in FlagReader.SplitTopLevel(toks.GetRange(0, Math.Min(close, n)), open + 1, ","))
                    {
                        int q = 0;
                        while (q < p.Count && (p[q].Is("Optional") || p[q].Is("ByVal") || p[q].Is("ByRef") || p[q].Is("ParamArray"))) q++;
                        if (q < p.Count) Shadow(p[q], line, "aynı isimde parametre (gölgeleme)");
                    }
                }
                // başlıktan sonra ':' ile gelen ifadeler ("Sub X(): A = True") aşağıda normal taranır
            }

            // Private bayrak (Private/Dim/kapsamsız Const) başka modülden erişilemez: orada adı başka bir şeydir
            foreach (var t in toks.Take(n))
                if (t.Kind == TokenKind.Identifier && flags.Get(t.Text) is { } pf && IsPrivate(pf) && privateReported.Add(pf.Name))
                    Shadow(t, line, "Private bayrak başka modülden kullanılıyor (VB'de erişilemez)");

            foreach (int s0 in StatementStarts(toks, n))
            {
                int s = s0;
                if (s >= n) continue;
                if (DeclWords.Any(toks[s].Is))
                {
                    // Dim/Private/... bildirimi: aynı isimde yerel değişken bayrağı gölgeler
                    int k = s;
                    while (k < n && (DeclWords.Any(toks[k].Is) || toks[k].Is("Preserve") || toks[k].Is("WithEvents"))) k++;
                    if (k < n && (toks[k].Is("Sub") || toks[k].Is("Function") || toks[k].Is("Property") || toks[k].Is("Declare")
                                  || toks[k].Is("Type") || toks[k].Is("Enum") || toks[k].Is("Event")))
                        continue;
                    var stmt = toks.GetRange(0, n);
                    foreach (var decl in FlagReader.SplitTopLevel(stmt, k, ","))
                    {
                        if (decl.Count == 0) continue;
                        Shadow(decl[0], line, "aynı isimle yeniden bildiriliyor (gölgeleme)");
                    }
                    continue;
                }
                if (toks[s].Is("For"))
                {
                    // For X = ... / For Each X In ...: döngü değişkenine atama
                    int v = s + 1 < n && toks[s + 1].Is("Each") ? s + 2 : s + 1;
                    if (v < n) Shadow(toks[v], line, "döngü değişkeni olarak atanıyor", otherType: false);
                    continue;
                }
                if (toks[s].Is("Let") || toks[s].Is("Set")) s++;
                if (s >= n || toks[s].Kind != TokenKind.Identifier) continue;
                int nameIdx = s;
                if (s + 2 < n && moduleNames.Contains(toks[s].Text) && toks[s + 1].Text == ".") nameIdx = s + 2;
                if (nameIdx + 1 < n && toks[nameIdx + 1].Text == "=" && flags.IsFlag(toks[nameIdx].Text))
                {
                    var name = flags.Get(toks[nameIdx].Text)!.Name;
                    flags.MarkUnknown(name, $"{rel}:{line.LineNumber} atama yapılıyor");
                    report.FlagAssignments.Add(new SourceNote(rel, line.LineNumber, $"{name} bayrağına atama yapılıyor", line.Text.Trim()));
                }
            }
        }
    }

    /// <summary>
    /// Bayrak modülündeki yordamların (InitFlags vb.) bu dosyada çağrıldığı yerler. Blok (If/Select/döngü)
    /// içindeki veya tek satırlık If'teki çağrılar koşullu işaretlenir.
    /// </summary>
    public static List<ProcedureCall> ScanProcedureCalls(VbSourceFile file, string rel, ISet<string> procNames, ISet<string> moduleNames,
        IReadOnlyList<LogicalLine>? lines = null)
    {
        var calls = new List<ProcedureCall>();
        string caller = "(modül)";
        int depth = 0;
        foreach (var line in lines ?? file.GetLogicalLines())
        {
            var toks = line.Tokens;
            int n = toks.Count - (Lexer.Comment(toks) != null ? 1 : 0);
            if (n == 0) continue;

            // yordam başı / sonu
            int k = 0;
            while (k < n && DeclWords.Any(toks[k].Is) || k < n && toks[k].Is("Friend")) k++;
            if (k < n && (toks[k].Is("Sub") || toks[k].Is("Function") || toks[k].Is("Property")))
            {
                int nameIdx = toks[k].Is("Property") ? k + 2 : k + 1;
                caller = $"{toks[k].Text} {(nameIdx < n ? toks[nameIdx].Text : "?")}";
                depth = 0;
                continue;
            }

            bool singleIf = false;
            var first = toks[toks[0].Kind == TokenKind.Number ? Math.Min(1, n - 1) : 0];
            if (first.Is("If"))
            {
                int then = toks.FindIndex(t => t.Is("Then"));
                singleIf = then >= 0 && then < n - 1;
            }
            foreach (int s0 in StatementStarts(toks, n))
            {
                int s = s0;
                if (s < n && toks[s].Is("Call")) s++;
                if (s + 2 < n && moduleNames.Contains(toks[s].Text) && toks[s + 1].Text == ".") s += 2;
                if (s < n && toks[s].Kind == TokenKind.Identifier && procNames.Contains(toks[s].Text)
                    && !(s + 1 < n && toks[s + 1].Text == "="))
                    calls.Add(new ProcedureCall(rel, line.LineNumber, caller, toks[s].Text, depth > 0 || singleIf, line.Text.Trim()));
            }

            // blok derinliği
            if (first.Is("End") && n > 1 && (toks[1].Is("Sub") || toks[1].Is("Function") || toks[1].Is("Property"))) { caller = "(modül)"; depth = 0; }
            else if (first.Is("End") || first.Is("Next") || first.Is("Loop") || first.Is("Wend")) depth = Math.Max(0, depth - 1);
            else if ((first.Is("If") && !singleIf) || first.Is("Select") || first.Is("For") || first.Is("Do") || first.Is("While") || first.Is("With")) depth++;
        }
        return calls;
    }

    /// <summary>İfade başlangıç token indeksleri: satır başı/etiket sonrası, ':' sonrası, Then/Else sonrası.</summary>
    private static IEnumerable<int> StatementStarts(List<Token> toks, int n)
    {
        int start = 0;
        if (toks[0].Kind == TokenKind.Number) start = 1;
        else if (n > 1 && toks[0].Kind == TokenKind.Identifier && toks[1].Text == ":") start = 2;
        yield return start;
        int depth = 0;
        for (int i = start; i < n; i++)
        {
            var t = toks[i];
            if (t.Text == "(") depth++;
            else if (t.Text == ")") depth--;
            else if (depth == 0 && (t.Text == ":" || t.Is("Then") || t.Is("Else"))) yield return i + 1;
        }
    }
}
