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
}

public sealed record ChangeRecord(int Line, ChangeKind Kind, string Description);

public sealed class FileReport
{
    public required string RelativePath { get; init; }
    public List<ChangeRecord> Changes { get; } = [];
    public List<SourceNote> Warnings { get; } = [];
    public bool Changed { get; set; }
    public bool Failed { get; set; }
    public int RemovedLines { get; set; }

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
        AppendNotes(sb, "Bayrak notları", FlagNotes);
        AppendNotes(sb, "Tanınmayan / atlanan satırlar (bayrak dosyası)", UnrecognizedFlagLines);
        AppendNotes(sb, "Bayrağa atama / yerel bildirim (Bilinmiyor sayıldı)", FlagAssignments);
        sb.AppendLine();

        sb.AppendLine("DOSYALAR");
        sb.AppendLine(new string('-', 60));
        var changed = Files.Where(f => f.Changed).ToList();
        sb.AppendLine($"İşlenen dosya : {Files.Count}  (değişecek: {changed.Count}, hatalı: {Files.Count(f => f.Failed)})");
        sb.AppendLine($"Toplam        : {Sum(ChangeKind.BlockRemoved)} blok silindi, {Sum(ChangeKind.BlockLifted)} blok açıldı, " +
                      $"{Sum(ChangeKind.BlockSimplified)} blok sadeleşti, {Sum(ChangeKind.SingleLineIf)} tek satırlık If, " +
                      $"{Files.Sum(f => f.RemovedLines)} satır azaldı");
        sb.AppendLine();
        foreach (var f in Files.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            if (!f.Changed && f.Warnings.Count == 0 && !f.Failed) continue;
            sb.AppendLine($"[{f.RelativePath}]{(f.Failed ? " HATA - dosya değiştirilmeden kopyalandı" : "")}");
            if (f.Changed)
                sb.AppendLine($"  {f.Count(ChangeKind.BlockRemoved)} silindi, {f.Count(ChangeKind.BlockLifted)} açıldı, " +
                              $"{f.Count(ChangeKind.BlockSimplified)} sadeleşti, {f.Count(ChangeKind.SingleLineIf)} tek satır, {f.RemovedLines} satır azaldı");
            foreach (var c in f.Changes)
                sb.AppendLine($"    satır {c.Line,5}: {c.Description}");
            foreach (var w in f.Warnings)
                sb.AppendLine($"  UYARI satır {w.Line,5}: {w.Message}{(w.Text != null ? "  | " + w.Text : "")}");
            sb.AppendLine();
        }
        int unchanged = Files.Count(f => !f.Changed && f.Warnings.Count == 0 && !f.Failed);
        if (unchanged > 0) sb.AppendLine($"Değişmeyen dosya: {unchanged}");
        if (CopiedFiles.Count > 0) sb.AppendLine($"Olduğu gibi kopyalanan diğer dosya: {CopiedFiles.Count}");
        sb.AppendLine($"Toplam uyarı: {WarningCount}");
        return sb.ToString();
    }

    private int Sum(ChangeKind k) => Files.Sum(f => f.Count(k));

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
