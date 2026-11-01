namespace VbFlagResolver.Core;

/// <summary>
/// Orijinal dosyalara yazmayı engeller. Yollar junction/symlink'ler çözülerek karşılaştırılır; böylece
/// kaynak klasörü gösteren bir bağlantı üzerinden de kaynağa yazılamaz.
/// </summary>
public sealed class WriteGuard
{
    private readonly string _src;
    private readonly string _flags;

    public WriteGuard(string sourceDir, string flagsPath)
    {
        _src = RealPath(sourceDir);
        _flags = RealPath(flagsPath);
    }

    /// <summary>Çıktı klasörü kaynak klasörle iç içe olamaz (aynısı, içi veya üstü).</summary>
    public void CheckOutputDir(string outDir)
    {
        var real = RealPath(outDir);
        if (ProjectProcessor.IsUnder(real, _src) || ProjectProcessor.IsUnder(_src, real))
            throw new ArgumentException("Çıktı klasörü kaynak klasörle aynı, onun içinde veya üst klasörü olamaz; orijinal dosyalara yazılmaz."
                                        + (Same(real, outDir) ? "" : $" (bağlantı çözüldü: {real})"));
        if (ProjectProcessor.IsUnder(_flags, real))
            throw new ArgumentException("Çıktı klasörü bayrak dosyasını içeremez; bayrak dosyasının üzerine yazılabilir.");
    }

    /// <summary>Yazılacak dosya (çıktı veya rapor) kaynak klasörde veya bayrak dosyasının kendisi olamaz.</summary>
    public void CheckFile(string path)
    {
        var real = RealPath(path);
        if (ProjectProcessor.IsUnder(real, _src))
            throw new InvalidOperationException($"Güvenlik: kaynak klasöre yazma engellendi: {real}");
        if (Same(real, _flags))
            throw new InvalidOperationException($"Güvenlik: bayrak dosyasının üzerine yazma engellendi: {real}");
    }

    private static bool Same(string a, string b)
        => string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>Yolun var olan her bileşenindeki junction/symlink'i hedefine çevirir; olmayan kısım olduğu gibi eklenir.</summary>
    public static string RealPath(string path) => RealPath(Path.GetFullPath(path), 0);

    private static string RealPath(string full, int depth)
    {
        var root = Path.GetPathRoot(full) ?? "";
        var parts = full[root.Length..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        var cur = root;
        foreach (var part in parts)
        {
            var next = Path.Combine(cur, part);
            FileSystemInfo fi = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);
            if (depth < 32 && fi.Exists && fi.LinkTarget != null)
            {
                var target = fi.ResolveLinkTarget(returnFinalTarget: true);
                if (target != null) next = RealPath(target.FullName, depth + 1);
            }
            cur = next;
        }
        return cur;
    }
}
