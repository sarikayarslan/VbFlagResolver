using VbFlagResolver.Core;

namespace VbFlagResolver.Tests;

internal static class TestPaths
{
    public static string CasesDir => Path.Combine(AppContext.BaseDirectory, "Cases");

    /// <summary>Depo kökündeki samples/ klasörü.</summary>
    public static string SamplesDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "samples")))
                dir = dir.Parent;
            return dir == null ? throw new DirectoryNotFoundException("samples klasörü bulunamadı") : Path.Combine(dir.FullName, "samples");
        }
    }

    public static string NewTempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "VbFlagResolverTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>Test bayrakları: T1/T2 True, F1/F2 False, U1 Bilinmiyor.</summary>
    public static FlagReadResult CaseFlags() => FlagReader.Read(Path.Combine(CasesDir, "_flags.bas"));

    public static RewriteResult Rewrite(string source, FlagTable? flags = null, string moduleName = "mFlags")
    {
        flags ??= CaseFlags().Flags;
        var file = VbSourceFile.FromText("Test.bas", source);
        return new Rewriter(flags, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { moduleName }).Rewrite(file);
    }

    public static string RewriteText(string source, FlagTable? flags = null)
        => VbSourceFile.GetEncoding(1254).GetString(Rewrite(source, flags).Output);
}
