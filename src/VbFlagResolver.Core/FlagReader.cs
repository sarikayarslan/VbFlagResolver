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

    // --- nasıl okunduğu (arayüz ve rapor için)

    /// <summary>Tanımın bulunduğu mantıksal satır (satır devamları birleşik).</summary>
    public string Declaration { get; init; } = "";
    /// <summary>Public / Private / Global / Dim ...; kapsamsız "Const X" için null.</summary>
    public string? Scope { get; init; }
    /// <summary>"Boolean" veya tipsizse null.</summary>
    public string? TypeName { get; init; }
    /// <summary>Aynı satırda virgülle birden çok tanım var mı?</summary>
    public bool SharedLine { get; init; }
    /// <summary>Tanım satır devamı ( _) ile birden çok fiziksel satıra yayılıyor mu?</summary>
    public bool Continued { get; init; }
    /// <summary>Sabitin değer ifadesi veya değişkene atanan ifade.</summary>
    public string? Expression { get; set; }
    /// <summary>Son (geçerli) atama.</summary>
    public int? AssignmentLine { get; set; }
    public string? AssignmentProcedure { get; set; }
    public string? AssignmentText { get; set; }
    /// <summary>Son atamadan önceki atamalar ("InitFlags satır 51: gbX = True").</summary>
    public List<string> PreviousAssignments { get; } = [];
    /// <summary>Koşullu atamadan gelen belirsizlik: aynı zincirde koşulsuz atama gelirse kalkar.</summary>
    internal (string Chain, string Reason)? PendingConditional { get; set; }
    /// <summary>Son atamanın yapıldığı çalıştırma zinciri (kök yordam veya verilen çağrı sırası).</summary>
    internal string? AssignmentChain { get; set; }

    /// <summary>Tanım biçimi, ör. "Public Const ... As Boolean".</summary>
    public string Style
    {
        get
        {
            var scope = Scope != null ? Scope + " " : "";
            return Kind == FlagKind.Const
                ? $"{scope}Const{(TypeName != null ? " As " + TypeName : " (tipsiz)")}"
                : $"{scope}... As Boolean (değişken)";
        }
    }

    /// <summary>Değerin nereden geldiğinin Türkçe açıklaması.</summary>
    public string HowRead
    {
        get
        {
            var parts = new List<string>();
            if (SharedLine) parts.Add("aynı satırda birden çok tanım");
            if (Continued) parts.Add("satır devamıyla");
            if (Kind == FlagKind.Const)
            {
                if (Expression != null && !IsLiteral(Expression)) parts.Add($"ifadeden hesaplandı: {Expression}");
                else if (Expression != null) parts.Add($"doğrudan değer: {Expression}");
            }
            else if (AssignmentLine != null)
            {
                parts.Add($"{AssignmentProcedure} içinde satır {AssignmentLine}: {AssignmentText}");
                if (PreviousAssignments.Count > 0)
                    parts.Add($"öncekiler: {string.Join(", ", PreviousAssignments)}" + (UnknownReason == null ? " → son atama geçerli" : ""));
            }
            else if (Value == false && UnknownReason == null)
            {
                parts.Add("hiç atanmamış → VB varsayılanı False");
            }
            if (UnknownReason != null) parts.Add("Bilinmiyor: " + UnknownReason);
            return string.Join("; ", parts);
        }
    }

    private static bool IsLiteral(string e) => e.Trim() is var t
        && (t.Equals("True", StringComparison.OrdinalIgnoreCase) || t.Equals("False", StringComparison.OrdinalIgnoreCase));
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

/// <summary>Bayrak modülündeki bir yordam.</summary>
/// <param name="AssignsFlags">İçinde bayrak ataması var mı?</param>
/// <param name="IsRoot">Kök olarak çalıştırıldı mı (verilen sıra veya modül içinde çağrılmayan)?</param>
/// <param name="Calls">Modül içinde çağırdığı yordamlar.</param>
public sealed record FlagProcedure(string Name, int Line, bool AssignsFlags, bool IsRoot, IReadOnlyList<string> Calls);

public sealed class FlagReadResult
{
    public required FlagTable Flags { get; init; }
    public required string ModuleName { get; init; }
    public List<SourceNote> Unrecognized { get; } = [];
    public List<SourceNote> Notes { get; } = [];
    public List<FlagProcedure> Procedures { get; } = [];
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
        // 1. aşama: bildirimler hemen okunur, yordam gövdeleri saklanır
        // 2. aşama: yordamlar çağrı sırasıyla "çalıştırılır" (metindeki sıra değil)
        var bodies = new List<ProcBody>();
        ProcBody? currentProc = null;
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
                    currentProc = new ProcBody(nameIdx < stmt.Count ? stmt[nameIdx].Text : "?", lineNo);
                    bodies.Add(currentProc);
                    continue;
                }
                if (first.Is("End") && stmt.Count > 1 && (stmt[1].Is("Sub") || stmt[1].Is("Function") || stmt[1].Is("Property")))
                {
                    currentProc = null;
                    continue;
                }

                if (currentProc != null)
                {
                    currentProc.Statements.Add(new BufferedStmt(stmt, line.Text, lineNo));
                    continue;
                }

                // modül düzeyi bildirimler
                int s = 0;
                if (s < stmt.Count && Scopes.Any(stmt[s].Is)) s++;
                bool isConst = s < stmt.Count && stmt[s].Is("Const");
                if (isConst) s++;
                if (s == 0 && !isConst)
                {
                    // "Bayrak = False" yordam dışında: VB'de derlenmez; niyet belirsiz olduğu için Bilinmiyor say
                    int a = first.Is("Let") ? 1 : 0;
                    if (stmt.Count > a + 2 && stmt[a + 1].Text == "=" && table.Get(stmt[a].Text) is { } assigned)
                    {
                        table.MarkUnknown(assigned.Name, $"{fileName}:{lineNo} yordam dışında atanıyor (VB'de derlenmez)");
                        result.Notes.Add(new SourceNote(fileName, lineNo,
                            $"{assigned.Name}: yordam dışında atama (End Sub/Function'dan sonra). VB bunu derlemez; bayrak Bilinmiyor sayıldı",
                            line.Text.Trim()));
                        Unrecognized("Yordam dışında bayrak ataması (VB'de derlenmez)");
                        continue;
                    }
                    Unrecognized("Tanınmayan modül düzeyi satır");
                    continue;
                }
                if (s < stmt.Count && (stmt[s].Is("Declare") || stmt[s].Is("Type") || stmt[s].Is("Enum") || stmt[s].Is("Event")))
                {
                    Unrecognized("Bayrak değil (Declare/Type/Enum/Event)");
                    continue;
                }

                var decls = SplitTopLevel(stmt, s, ",").ToList();
                var ctx = new DeclContext(stmt[0].Kind == TokenKind.Identifier && Scopes.Any(stmt[0].Is) ? stmt[0].Text : null,
                    line.Text.Trim(), decls.Count > 1, line.Count > 1);
                foreach (var decl in decls)
                {
                    if (isConst)
                    {
                        if (options.Mode == FlagReadMode.VariablesOnly) { Unrecognized("Sabit atlandı (yalnızca değişken modu)"); continue; }
                        ReadConst(decl, line.Text, ctx, table, moduleNames, fileName, lineNo, result, Unrecognized);
                    }
                    else
                    {
                        if (options.Mode == FlagReadMode.ConstOnly) { Unrecognized("Değişken atlandı (yalnızca sabit modu)"); continue; }
                        ReadVariable(decl, ctx, table, lineNo, Unrecognized);
                    }
                }
            }
        }

        RunProcedures(bodies, new ExecContext
        {
            Table = table, Options = options, ModuleNames = moduleNames, FileName = fileName, Result = result,
            Bodies = bodies.GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase),
        });

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

    /// <summary>Yordam gövdesindeki bir ifade (2. aşamada çalıştırılmak üzere saklanır).</summary>
    private sealed record BufferedStmt(List<Token> Stmt, string Text, int LineNo);

    private sealed class ProcBody(string name, int line)
    {
        public string Name { get; } = name;
        public int Line { get; } = line;
        public List<BufferedStmt> Statements { get; } = [];
    }

    private sealed class ExecContext
    {
        public required FlagTable Table { get; init; }
        public required FlagReaderOptions Options { get; init; }
        public required ISet<string> ModuleNames { get; init; }
        public required string FileName { get; init; }
        public required FlagReadResult Result { get; init; }
        public required Dictionary<string, ProcBody> Bodies { get; init; }
        /// <summary>Kullanıcı çağrı sırasını verdi: yordamlar art arda çalışır, son atama geçerli.</summary>
        public bool Ordered { get; set; }
        /// <summary>Şu an çalıştırılan kök yordam (sıra bilinmiyorsa farklı kökler birbirinden bağımsızdır).</summary>
        public string Root { get; set; } = "";
        public string ChainKey => Ordered ? "*" : Root;
        public HashSet<string> Executed { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Çalıştırılmakta olan yordamın durumu: blok derinliği, Exit ile çıkış, koşullu çağrı.</summary>
    private sealed class ProcState(string name)
    {
        public string Name { get; } = name;
        public int BlockDepth { get; set; }
        /// <summary>Blok dışında Exit Sub/Function görüldü: sonraki satırlar çalışmaz.</summary>
        public bool ExitedUnconditionally { get; set; }
        /// <summary>Blok içinde Exit görüldü: sonraki atamalar koşullu sayılır.</summary>
        public bool ExitedConditionally { get; set; }
        /// <summary>Bu yordam koşullu bir yerden çağrıldı.</summary>
        public bool CalledConditionally { get; init; }
        public bool IsConditional => BlockDepth > 0 || ExitedConditionally || CalledConditionally;
    }

    /// <summary>
    /// Kök yordamları belirler ve çalıştırır. Kökler: kullanıcının verdiği sıra (InitProcedures) ya da
    /// modül içinde başka yordamdan çağrılmayan yordamlar. Modül içi çağrılar yerinde (inline) izlenir.
    /// </summary>
    private static void RunProcedures(List<ProcBody> bodies, ExecContext ctx)
    {
        var options = ctx.Options;
        var result = ctx.Result;
        var calledInside = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in bodies)
        foreach (var st in b.Statements)
        foreach (var c in FindCalls(st.Stmt, ctx.Bodies))
            if (!c.Equals(b.Name, StringComparison.OrdinalIgnoreCase)) calledInside.Add(c);

        List<ProcBody> roots;
        if (options.InitProcedures is { Count: > 0 })
        {
            ctx.Ordered = true;
            roots = [];
            foreach (var n in options.InitProcedures)
            {
                if (ctx.Bodies.TryGetValue(n, out var b)) roots.Add(b);
                else result.Notes.Add(new SourceNote(ctx.FileName, 0, $"İlk değer yordamı bulunamadı: {n}"));
            }
        }
        else
        {
            roots = bodies.Where(b => !calledInside.Contains(b.Name)).ToList();
        }

        foreach (var root in roots)
        {
            ctx.Root = root.Name;
            Execute(root, ctx, conditional: false, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root.Name });
        }

        // hiçbir kökten çağrılmayan yordamlardaki atamalar: ne zaman çalıştığı bilinmez
        foreach (var b in bodies.Where(b => !ctx.Executed.Contains(b.Name)))
        foreach (var st in b.Statements)
        {
            var stmt = st.Stmt;
            int s = stmt[0].Is("Let") ? 1 : 0;
            if (stmt.Count > s + 2 && stmt[s + 1].Text == "=" && ctx.Table.Get(stmt[s].Text) is { Kind: FlagKind.Variable } flag)
            {
                ctx.Table.MarkUnknown(flag.Name, $"{ctx.FileName}:{st.LineNo} {b.Name} içinde atanıyor (ilk değer yordamlarından çağrılmıyor)");
                flag.Assigned = true;
            }
        }

        var assigning = bodies.Where(b => b.Statements.Any(st => IsFlagAssignment(st.Stmt, ctx.Table))).Select(b => b.Name).ToList();
        foreach (var b in bodies)
            result.Procedures.Add(new FlagProcedure(b.Name, b.Line, assigning.Contains(b.Name),
                roots.Contains(b), b.Statements.SelectMany(st => FindCalls(st.Stmt, ctx.Bodies)).Distinct().ToList()));

        var assigningRoots = roots.Where(r => assigning.Contains(r.Name) || result.Procedures.Single(p => p.Name == r.Name).Calls.Count > 0).ToList();
        if (!ctx.Ordered && assigningRoots.Count > 1)
            result.Notes.Add(new SourceNote(ctx.FileName, 0,
                $"Birbirini çağırmayan birden çok bayrak yordamı var ({string.Join(", ", assigningRoots.Select(r => r.Name))}); " +
                "çağrı sırası bilinmiyor. Aynı bayrağa farklı değer verenler Bilinmiyor sayıldı. " +
                "Sırayı 'Çağrı sırası' (--init-proc) ile, önce çağrılan önce olacak şekilde belirtin"));
    }

    private static bool IsFlagAssignment(List<Token> stmt, FlagTable table)
    {
        int s = stmt[0].Is("Let") ? 1 : 0;
        return stmt.Count > s + 2 && stmt[s + 1].Text == "=" && table.IsFlag(stmt[s].Text);
    }

    /// <summary>İfadedeki modül içi yordam çağrıları: "X", "Call X(...)", tek satırlık If'in Then/Else kısmı.</summary>
    private static List<string> FindCalls(List<Token> stmt, Dictionary<string, ProcBody> bodies)
    {
        var calls = new List<string>();
        var starts = new List<int> { 0 };
        for (int i = 0; i < stmt.Count; i++)
            if (stmt[i].Is("Then") || stmt[i].Is("Else")) starts.Add(i + 1);
        foreach (int s0 in starts)
        {
            int i = s0;
            if (i < stmt.Count && stmt[i].Is("Call")) i++;
            if (i < stmt.Count && stmt[i].Kind == TokenKind.Identifier && bodies.TryGetValue(stmt[i].Text, out var b)
                && !(i + 1 < stmt.Count && stmt[i + 1].Text == "="))
                calls.Add(b.Name);
        }
        return calls;
    }

    private static void Execute(ProcBody body, ExecContext ctx, bool conditional, HashSet<string> stack)
    {
        ctx.Executed.Add(body.Name);
        var state = new ProcState(body.Name) { CalledConditionally = conditional };
        foreach (var st in body.Statements)
        {
            var calls = FindCalls(st.Stmt, ctx.Bodies);
            bool singleIf = st.Stmt[0].Is("If") && IsSingleLineIf(st.Stmt);
            if (calls.Count > 0)
            {
                foreach (var c in calls)
                {
                    if (state.ExitedUnconditionally) break;
                    if (stack.Contains(c)) continue; // özyineleme: bir kez izlemek yeter
                    Execute(ctx.Bodies[c], ctx, state.IsConditional || singleIf, new HashSet<string>(stack, StringComparer.OrdinalIgnoreCase) { c });
                }
                if (!singleIf) continue;
            }
            ReadProcedureStatement(st, state, ctx);
        }
    }

    private static void ReadProcedureStatement(BufferedStmt st, ProcState proc, ExecContext ctx)
    {
        var (stmt, text, lineNo) = st;
        var table = ctx.Table;
        var result = ctx.Result;
        string fileName = ctx.FileName;
        void unrecognized(string why)
        {
            if (!result.Unrecognized.Any(u => u.Line == lineNo && u.Message == why))
                result.Unrecognized.Add(new SourceNote(fileName, lineNo, why, text.Trim()));
        }

        var first = stmt[0];
        // blok derinliği (koşullu atamaları ayırt etmek için)
        if (first.Is("End") || first.Is("Next") || first.Is("Loop") || first.Is("Wend"))
        {
            proc.BlockDepth = Math.Max(0, proc.BlockDepth - 1);
            return;
        }
        if (first.Is("Exit") && stmt.Count > 1 && (stmt[1].Is("Sub") || stmt[1].Is("Function") || stmt[1].Is("Property")))
        {
            if (proc.BlockDepth == 0) proc.ExitedUnconditionally = true;
            else proc.ExitedConditionally = true;
            return;
        }
        if (first.Is("Exit") || first.Is("Else") || first.Is("ElseIf") || first.Is("Case")) return;
        if (BlockOpen.Any(first.Is))
        {
            bool singleLineIf = first.Is("If") && IsSingleLineIf(stmt);
            if (singleLineIf && stmt.Any(t => t.Is("Exit"))) proc.ExitedConditionally = true;
            if (!singleLineIf) proc.BlockDepth++;
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
            if (ctx.Options.Mode == FlagReadMode.ConstOnly) { unrecognized("Değişken ataması atlandı (yalnızca sabit modu)"); return; }

            string assignText = text[stmt[0].Start..stmt[^1].End];
            string chain = ctx.ChainKey;
            if (proc.ExitedUnconditionally)
            {
                unrecognized($"Erişilemez atama (yukarıda Exit {proc.Name} var), dikkate alınmadı");
                return;
            }
            if (proc.IsConditional)
            {
                string reason = proc.BlockDepth > 0 ? $"{fileName}:{lineNo} koşullu/döngü içinde atanıyor"
                    : proc.ExitedConditionally ? $"{fileName}:{lineNo} koşullu Exit'ten sonra atanıyor"
                    : $"{fileName}:{lineNo} koşullu çağrılan {proc.Name} içinde atanıyor";
                bool wasClean = flag.UnknownReason == null;
                table.MarkUnknown(flag.Name, reason);
                // aynı zincirde sonradan koşulsuz atama gelirse bu belirsizlik kalkar
                flag.PendingConditional = wasClean ? (chain, flag.UnknownReason!) : null;
                RecordAssignment(flag, lineNo, proc.Name, assignText, chain);
                flag.Assigned = true;
                return;
            }

            var exprTokens = stmt.GetRange(s + 2, stmt.Count - s - 2);
            bool? value = Evaluate(exprTokens, text, table, ctx.ModuleNames);
            // aynı zincir: aynı kök yordamın çalışması veya kullanıcının verdiği çağrı sırası
            bool sameChain = flag.Assigned && flag.AssignmentChain == chain;

            if (flag.PendingConditional is var (pendingChain, pendingReason) && pendingChain == chain && flag.UnknownReason == pendingReason)
            {
                // koşullu atamadan sonra gelen koşulsuz atama her durumda son sözü söyler
                flag.UnknownReason = null;
                flag.PendingConditional = null;
                flag.Value = value;
                RecordAssignment(flag, lineNo, proc.Name, assignText, chain);
                flag.Expression = text[exprTokens[0].Start..exprTokens[^1].End];
                if (value == null) table.MarkUnknown(flag.Name, $"{fileName}:{lineNo} değer hesaplanamadı");
                return;
            }

            if (value == null)
            {
                table.MarkUnknown(flag.Name, $"{fileName}:{lineNo} değer hesaplanamadı");
            }
            else if (sameChain && flag.UnknownReason == null)
            {
                // sırayla atama: VB'de son atama geçerlidir
                if (flag.Value != value)
                {
                    bool otherProc = !string.Equals(flag.AssignmentProcedure, proc.Name, StringComparison.OrdinalIgnoreCase);
                    result.Notes.Add(new SourceNote(fileName, lineNo,
                        $"{flag.Name}: {flag.AssignmentProcedure} satır {flag.AssignmentLine}'de {Bool(flag.Value)}, " +
                        $"{proc.Name} satır {lineNo}'de {Bool(value)} atanıyor; son atama ({Bool(value)}) geçerli" +
                        (otherProc ? " (çağrı sırasına göre)" : "")));
                }
                flag.Value = value;
            }
            else if (flag.Assigned && !sameChain && flag.Value != value && flag.UnknownReason == null)
            {
                table.MarkUnknown(flag.Name,
                    $"{flag.AssignmentProcedure} içinde {Bool(flag.Value)} (satır {flag.AssignmentLine}), {proc.Name} içinde {Bool(value)} (satır {lineNo}); " +
                    "hangisinin son çalıştığı bilinmiyor. Çağrı sırasını 'Çağrı sırası' (--init-proc) ile belirtin, önce çağrılan önce yazılır");
            }
            else if (flag.UnknownReason == null)
            {
                flag.Value = value;
            }
            RecordAssignment(flag, lineNo, proc.Name, assignText, chain);
            flag.Expression = text[exprTokens[0].Start..exprTokens[^1].End];
            flag.Assigned = true;
            return;
        }
        unrecognized("Tanınmayan yordam satırı");
    }

    private static string Bool(bool? v) => v switch { true => "True", false => "False", _ => "?" };

    /// <summary>Son atamayı kaydeder; öncekini geçmişe ekler.</summary>
    private static void RecordAssignment(FlagInfo flag, int lineNo, string proc, string text, string chain)
    {
        if (flag.AssignmentLine is int prev)
            flag.PreviousAssignments.Add($"{flag.AssignmentProcedure} satır {prev}: {flag.AssignmentText}");
        flag.AssignmentLine = lineNo;
        flag.AssignmentProcedure = proc;
        flag.AssignmentText = text;
        flag.AssignmentChain = chain;
    }

    internal static bool IsSingleLineIf(List<Token> stmt)
    {
        int then = stmt.FindIndex(t => t.Is("Then"));
        return then >= 0 && then < stmt.Count - 1 && stmt[then + 1].Kind != TokenKind.Comment;
    }

    /// <summary>Bir bildirim satırının ortak bilgileri (kapsam, kaynak metin, çoklu tanım, satır devamı).</summary>
    private sealed record DeclContext(string? Scope, string Declaration, bool SharedLine, bool Continued);

    private static void ReadConst(List<Token> decl, string text, DeclContext ctx, FlagTable table, ISet<string> moduleNames,
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
        if (value == null && type == null && !LooksBoolean(exprTokens, table))
        {
            unrecognized($"Boolean olmayan sabit: {name}");
            return;
        }
        var info = new FlagInfo
        {
            Name = name, Kind = FlagKind.Const, Value = value, Line = lineNo,
            UnknownReason = value == null ? "değeri hesaplanamadı" : null,
            Declaration = ctx.Declaration, Scope = ctx.Scope, TypeName = type, SharedLine = ctx.SharedLine, Continued = ctx.Continued,
            Expression = text[exprTokens[0].Start..exprTokens[^1].End],
        };
        if (!table.Add(info))
            result.Notes.Add(new SourceNote(fileName, lineNo, $"{name} birden çok kez tanımlanmış; ilk tanım kullanıldı"));
    }

    private static bool LooksBoolean(List<Token> tokens, FlagTable table)
        => tokens.Any(t => t.Is("True") || t.Is("False") || (t.Kind == TokenKind.Identifier && table.IsFlag(t.Text)));

    private static void ReadVariable(List<Token> decl, DeclContext ctx, FlagTable table, int lineNo, Action<string> unrecognized)
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
        table.Add(new FlagInfo
        {
            Name = name, Kind = FlagKind.Variable, Value = null, Line = lineNo,
            Declaration = ctx.Declaration, Scope = ctx.Scope, TypeName = "Boolean", SharedLine = ctx.SharedLine, Continued = ctx.Continued,
        });
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
