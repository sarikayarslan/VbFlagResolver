using System.Text;

namespace VbFlagResolver.Core;

public enum ChangeKind
{
    /// <summary>Tüm If bloğu silindi (hiçbir dal kalmadı).</summary>
    BlockRemoved,
    /// <summary>If kalktı, True dalın içeriği yerine kondu.</summary>
    BlockLifted,
    /// <summary>Blok korundu; dallar silindi ve/veya koşullar sadeleşti.</summary>
    BlockSimplified,
    SingleLineIf,
    /// <summary>#If grubu derleme sabitlerine göre çözüldü.</summary>
    DirectiveResolved,
}

public sealed record ChangeRecord(int Line, ChangeKind Kind, string Description);

/// <summary>Bayrak modülündeki bir yordamın proje içinde çağrıldığı yer.</summary>
/// <param name="Conditional">Çağrı If/Select/döngü içinde mi?</param>
public sealed record ProcedureCall(string File, int Line, string Caller, string Procedure, bool Conditional, string Text);

public sealed class FileReport
{
    public required string RelativePath { get; init; }
    public List<ChangeRecord> Changes { get; } = [];
    public List<SourceNote> Warnings { get; } = [];
    public bool Changed { get; set; }
    public bool Failed { get; set; }
    /// <summary>Kullanıcı işaretledi: dönüştürülmeden olduğu gibi aktarıldı (atama/gölgeleme taraması yine yapılır).</summary>
    public bool KeptAsIs { get; set; }
    public int RemovedLines { get; set; }
    /// <summary>Önizleme (fark gösterimi) için orijinal ve üretilen içerik.</summary>
    public byte[]? OriginalBytes { get; set; }
    public byte[]? OutputBytes { get; set; }

    public int Count(ChangeKind k) => Changes.Count(c => c.Kind == k);
}

/// <summary>Değişiklik ve uyarı raporu.</summary>
public sealed class Report
{
    public string FlagsFile { get; set; } = "";
    public string SourceDir { get; set; } = "";
    public string? OutputDir { get; set; }
    public bool DryRun { get; set; }
    public FlagTable Flags { get; set; } = new();
    public List<SourceNote> FlagNotes { get; } = [];
    public List<SourceNote> UnrecognizedFlagLines { get; } = [];
    /// <summary>Bayrağa atama / yerel bildirim nedeniyle Bilinmiyor sayılanlar.</summary>
    public List<SourceNote> FlagAssignments { get; } = [];
    /// <summary>Proje derleme sabitleri (.vbp CondComp + kullanıcı).</summary>
    public CondConstants Constants { get; set; } = new();
    public bool ResolveDirectives { get; set; } = true;
    public List<FlagProcedure> FlagProcedures { get; } = [];
    /// <summary>Bayrak yordamlarının projedeki çağrıları (kaynak sırasıyla).</summary>
    public List<ProcedureCall> ProcedureCalls { get; } = [];
    public List<FileReport> Files { get; } = [];
    public List<string> CopiedFiles { get; } = [];
    public Dictionary<string, int> FlagUsage { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int WarningCount => Files.Sum(f => f.Warnings.Count) + FlagAssignments.Count;

    public string ToText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("VbFlagResolver raporu");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine($"Tarih         : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Bayrak dosyası: {FlagsFile}");
        sb.AppendLine($"Kaynak klasör : {SourceDir}");
        sb.AppendLine($"Çıktı klasörü : {(DryRun ? "(kuru çalıştırma - dosya yazılmadı)" : OutputDir)}");
        sb.AppendLine();

        var all = Flags.All.ToList();
        int t = all.Count(f => f.Value == true), fa = all.Count(f => f.Value == false), u = all.Count(f => f.Value == null);
        sb.AppendLine("BAYRAKLAR");
        sb.AppendLine(new string('-', 60));
        sb.AppendLine($"Okunan bayrak : {all.Count}  (True: {t}, False: {fa}, Bilinmiyor: {u})");
        sb.AppendLine($"  Sabit       : {all.Count(f => f.Kind == FlagKind.Const)}");
        sb.AppendLine($"  Değişken    : {all.Count(f => f.Kind == FlagKind.Variable)}");
        foreach (var f in all.Where(f => f.Value == null).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"  Bilinmiyor: {f.Name} — {f.UnknownReason}");
        var unused = all.Where(f => !FlagUsage.ContainsKey(f.Name)).Select(f => f.Name).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        if (unused.Count > 0)
            sb.AppendLine($"  Hiçbir kaynak dosyada kullanılmayan bayrak: {unused.Count} ({Abbrev(unused, 30)})");
        AppendProcedures(sb);
        AppendConstants(sb);
        AppendNotes(sb, "Bayrak notları", FlagNotes);
        AppendNotes(sb, "Tanınmayan / atlanan satırlar (bayrak dosyası)", UnrecognizedFlagLines);
        AppendNotes(sb, "Bayrağa atama / yerel bildirim (Bilinmiyor sayıldı)", FlagAssignments);
        sb.AppendLine();

        sb.AppendLine("DOSYALAR");
        sb.AppendLine(new string('-', 60));
        var changed = Files.Where(f => f.Changed).ToList();
        var kept = Files.Where(f => f.KeptAsIs).ToList();
        sb.AppendLine($"İşlenen dosya : {Files.Count}  (değişecek: {changed.Count}, hatalı: {Files.Count(f => f.Failed)}" +
                      $"{(kept.Count > 0 ? $", olduğu gibi aktarılan: {kept.Count}" : "")})");
        if (kept.Count > 0)
            sb.AppendLine($"Olduğu gibi aktarılan (işaretli): {Abbrev(kept.Select(f => f.RelativePath).ToList(), 30)}");
        sb.AppendLine($"Toplam        : {Sum(ChangeKind.BlockRemoved)} blok silindi, {Sum(ChangeKind.BlockLifted)} blok açıldı, " +
                      $"{Sum(ChangeKind.BlockSimplified)} blok sadeleşti, {Sum(ChangeKind.SingleLineIf)} tek satırlık If, " +
                      $"{Sum(ChangeKind.DirectiveResolved)} #If çözüldü, " +
                      $"{Files.Sum(f => f.RemovedLines)} satır azaldı");
        sb.AppendLine();
        foreach (var f in Files.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            if (!f.Changed && f.Warnings.Count == 0 && !f.Failed) continue;
            sb.AppendLine($"[{f.RelativePath}]{(f.Failed ? " HATA - dosya değiştirilmeden kopyalandı" : "")}");
            if (f.Changed)
                sb.AppendLine($"  {f.Count(ChangeKind.BlockRemoved)} silindi, {f.Count(ChangeKind.BlockLifted)} açıldı, " +
                              $"{f.Count(ChangeKind.BlockSimplified)} sadeleşti, {f.Count(ChangeKind.SingleLineIf)} tek satır, " +
                              $"{f.Count(ChangeKind.DirectiveResolved)} #If, {f.RemovedLines} satır azaldı");
            foreach (var c in f.Changes)
                sb.AppendLine($"    satır {c.Line,5}: {c.Description}");
            foreach (var w in f.Warnings)
                sb.AppendLine($"  UYARI satır {w.Line,5}: {w.Message}{(w.Text != null ? "  | " + w.Text : "")}");
            sb.AppendLine();
        }
        int unchanged = Files.Count(f => !f.Changed && f.Warnings.Count == 0 && !f.Failed && !f.KeptAsIs);
        if (unchanged > 0) sb.AppendLine($"Değişmeyen dosya: {unchanged}");
        if (CopiedFiles.Count > 0) sb.AppendLine($"Olduğu gibi kopyalanan diğer dosya: {CopiedFiles.Count}");
        sb.AppendLine($"Toplam uyarı: {WarningCount}");
        return sb.ToString();
    }

    private int Sum(ChangeKind k) => Files.Sum(f => f.Count(k));

    /// <summary>#If koşullarında kullanılan proje sabitleri ve kaynakları.</summary>
    public void AppendConstants(StringBuilder sb)
    {
        if (!ResolveDirectives)
        {
            sb.AppendLine("Koşullu derleme (#If): çözülmüyor (kapalı); #If içindeki bayrak tanımları/atamaları Bilinmiyor sayılır");
            return;
        }
        if (Constants.Count == 0)
        {
            sb.AppendLine("Koşullu derleme sabitleri: yok (.vbp'de CondComp bulunamadı). Tanımsız sabit içeren #If grupları olduğu gibi bırakılır.");
            return;
        }
        sb.AppendLine($"Koşullu derleme sabitleri: {Constants.Count}");
        foreach (var (name, value, source) in Constants.All)
            sb.AppendLine($"  {name} = {(value is { } v ? v.ToString() : "bilinmiyor")}  ({source})");
    }

    /// <summary>Bayrak yordamları, çalıştırma kökleri ve projede çağrıldıkları yerler.</summary>
    public void AppendProcedures(StringBuilder sb)
    {
        if (FlagProcedures.Count == 0) return;
        sb.AppendLine($"Bayrak modülündeki yordamlar: {FlagProcedures.Count}");
        foreach (var p in FlagProcedures)
        {
            var tags = new List<string>();
            if (p.IsRoot) tags.Add("kök (çalıştırıldı)");
            if (p.AssignsFlags) tags.Add("bayrak atıyor");
            if (p.Calls.Count > 0) tags.Add("çağırdıkları: " + string.Join(", ", p.Calls));
            sb.AppendLine($"  {p.Name} (satır {p.Line}){(tags.Count > 0 ? " — " + string.Join("; ", tags) : "")}");
        }
        if (ProcedureCalls.Count > 0)
        {
            sb.AppendLine("Projede çağrıldıkları yerler (çağrı sırasını belirlemek için):");
            foreach (var g in ProcedureCalls.GroupBy(c => (c.File, c.Caller)))
                sb.AppendLine($"  {g.Key.File}, {g.Key.Caller}: " +
                              string.Join(" → ", g.Select(c => $"{c.Procedure} (satır {c.Line}{(c.Conditional ? ", koşullu" : "")})")));
        }
        if (SuggestInitOrder() is var (order, source))
            sb.AppendLine($"Önerilen çağrı sırası: {string.Join(",", order)}  ({source} içindeki sıraya göre; --init-proc {string.Join(",", order)})");
    }

    /// <summary>
    /// Birbirini çağırmayan, bayrak atayan yordamların tamamını koşulsuz çağıran tek bir yer varsa oradaki sırayı önerir
    /// (ör. Sub Main: VarsayilanlariYukle → InitFlags). Farklı yerler farklı sıra veriyorsa öneri yapılmaz.
    /// </summary>
    public (List<string> Order, string Source)? SuggestInitOrder()
    {
        var calledInside = new HashSet<string>(FlagProcedures.SelectMany(p => p.Calls), StringComparer.OrdinalIgnoreCase);
        var targets = FlagProcedures.Where(p => p.AssignsFlags && !calledInside.Contains(p.Name))
            .Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (targets.Count < 2) return null;

        var candidates = new List<(List<string> Order, string Source)>();
        foreach (var g in ProcedureCalls.Where(c => !c.Conditional).GroupBy(c => (c.File, c.Caller)))
        {
            var seq = g.Select(c => c.Procedure).Where(targets.Contains)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (seq.Count == targets.Count) candidates.Add((seq, $"{g.Key.File}, {g.Key.Caller}"));
        }
        var distinct = candidates.Select(c => string.Join(",", c.Order).ToLowerInvariant()).Distinct().Count();
        return distinct == 1 ? candidates[0] : null;
    }

    private static string Abbrev(List<string> items, int max)
        => string.Join(", ", items.Take(max)) + (items.Count > max ? ", ..." : "");

    private static void AppendNotes(StringBuilder sb, string title, List<SourceNote> notes)
    {
        if (notes.Count == 0) return;
        sb.AppendLine($"{title}: {notes.Count}");
        foreach (var n in notes)
            sb.AppendLine($"  {n.File}:{n.Line}: {n.Message}{(n.Text != null ? "  | " + n.Text : "")}");
    }
}
