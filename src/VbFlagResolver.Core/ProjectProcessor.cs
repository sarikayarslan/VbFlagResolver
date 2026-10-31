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
        if (!o.DryRun)
        {
            if (outDir == null) throw new ArgumentException("--out gerekli (veya --dry-run kullanın)");
            if (SamePath(outDir, src) || IsUnder(src, outDir))
                throw new ArgumentException("Çıktı klasörü kaynak klasörle aynı veya onun üst klasörü olamaz; orijinal dosyalara yazılmaz.");
        }

        var report = new Report { FlagsFile = flagsPath, SourceDir = src, OutputDir = outDir, DryRun = o.DryRun };

        // 1) bayraklar
        var flagOptions = new FlagReaderOptions
        {
            Mode = o.FlagOptions.Mode,
            InitProcedures = o.FlagOptions.InitProcedures,
            UnassignedVariableIsFalse = o.FlagOptions.UnassignedVariableIsFalse,
            CodePage = o.CodePage,
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

        // 3) bayrağa atama / gölgeleme taraması
        foreach (var (rel, file) in sources)
            ScanAssignments(file, rel, flagResult.Flags, moduleNames, report);

        // 4) dönüşüm
        var outputs = new List<(string Rel, byte[] Bytes)>();
        var rewriter = new Rewriter(flagResult.Flags, moduleNames);
        foreach (var (rel, file) in sources)
        {
            var r = rewriter.Rewrite(file, rel);
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
            foreach (var (rel, bytes) in outputs) Write(outDir!, src, rel, bytes);
            // bayrak dosyası kaynak klasördeyse "others" içindedir ve aynen kopyalanır
            if (o.CopyOtherFiles)
                foreach (var f in others) Write(outDir!, src, Path.GetRelativePath(src, f), File.ReadAllBytes(f));
        }
        return report;
    }

    private static void Write(string outDir, string src, string rel, byte[] bytes)
    {
        var target = Path.GetFullPath(Path.Combine(outDir, rel));
        if (IsUnder(target, src) && !IsUnder(target, outDir))
            throw new InvalidOperationException($"Güvenlik: kaynak klasöre yazma girişimi engellendi: {target}");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllBytes(target, bytes);
    }

    private static bool SamePath(string a, string b)
        => string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>path, dir'in içinde mi (aynısı dahil)?</summary>
    private static bool IsUnder(string path, string dir)
    {
        var p = Path.GetFullPath(path).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        var d = Path.GetFullPath(dir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return p.StartsWith(d, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly string[] DeclWords = ["Dim", "Static", "Private", "Public", "Global", "Const", "ReDim"];

    /// <summary>
    /// Bayrağa atama (FlagX = ...) veya aynı isimde yerel bildirim varsa bayrağı Bilinmiyor yapar ve raporlar.
    /// </summary>
    public static void ScanAssignments(VbSourceFile file, string rel, FlagTable flags, ISet<string> moduleNames, Report report)
    {
        foreach (var line in file.GetLogicalLines())
        {
            var toks = line.Tokens;
            int n = toks.Count - (Lexer.Comment(toks) != null ? 1 : 0);
            if (n == 0) continue;
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
                        if (decl.Count == 0 || decl[0].Kind != TokenKind.Identifier || !flags.IsFlag(decl[0].Text)) continue;
                        var name = flags.Get(decl[0].Text)!.Name;
                        flags.MarkUnknown(name, $"{rel}:{line.LineNumber} aynı isimde bildirim (gölgeleme)");
                        report.FlagAssignments.Add(new SourceNote(rel, line.LineNumber, $"{name} aynı isimle yeniden bildiriliyor", line.Text.Trim()));
                    }
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
