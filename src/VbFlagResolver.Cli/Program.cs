using System.Text;
using VbFlagResolver.Core;

namespace VbFlagResolver.Cli;

internal static class Program
{
    private const string Usage = """
        VbFlagResolver - VB5 geliştirme bayraklarına göre If bloklarını sadeleştirir.

        Kullanım:
          VbFlagResolver --flags <mDevelopmentFlags.bas> --src <kaynak klasör> --out <çıktı klasörü> [seçenekler]
          VbFlagResolver --flags <...> --src <...> --dry-run [--report rapor.txt]

        Seçenekler:
          --flags <dosya>        Bayrak modülü (.bas). Zorunlu.
          --src <klasör>         VB5 proje klasörü (alt klasörler dahil). Zorunlu.
          --out <klasör>         Çıktı klasörü. Orijinal dosyalara asla yazılmaz.
          --dry-run              Dosya yazmadan yalnızca rapor üret.
          --report <dosya>       Raporu dosyaya da yaz (UTF-8).
          --flag-mode <mod>      auto (varsayılan) | const | var
          --init-proc <adlar>    Değişken bayraklara değer veren yordam(lar), virgülle. Varsayılan: modüldeki tüm yordamlar.
          --unassigned-unknown   Hiç atanmamış Boolean değişken bayrağını False değil Bilinmiyor say.
          --ext <uzantılar>      İşlenecek uzantılar, virgülle. Varsayılan: .frm,.bas,.cls,.ctl,.dob,.pag
          --no-copy              Kaynak olmayan dosyaları (.frx, .vbp, ...) çıktıya kopyalama.
          --codepage <n>         Kaynak kod sayfası. Varsayılan: 1254
          -h, --help             Bu yardımı göster.

        Çıkış kodu: 0 başarılı, 1 hatalı parametre, 2 çalışma hatası.
        """;

    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { /* yönlendirilmiş çıktı */ }

        string? flags = null, src = null, outDir = null, reportPath = null;
        bool dryRun = false, noCopy = false, unassignedUnknown = false;
        var mode = FlagReadMode.Auto;
        string[]? initProcs = null;
        string[]? exts = null;
        int codePage = 1254;

        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{a} için değer eksik");
                switch (a.ToLowerInvariant())
                {
                    case "--flags": flags = Next(); break;
                    case "--src": src = Next(); break;
                    case "--out": outDir = Next(); break;
                    case "--report": reportPath = Next(); break;
                    case "--dry-run": dryRun = true; break;
                    case "--no-copy": noCopy = true; break;
                    case "--unassigned-unknown": unassignedUnknown = true; break;
                    case "--flag-mode":
                        mode = Next().ToLowerInvariant() switch
                        {
                            "auto" => FlagReadMode.Auto,
                            "const" => FlagReadMode.ConstOnly,
                            "var" => FlagReadMode.VariablesOnly,
                            var m => throw new ArgumentException($"Geçersiz --flag-mode: {m}"),
                        };
                        break;
                    case "--init-proc": initProcs = SplitList(Next()); break;
                    case "--ext": exts = SplitList(Next()).Select(e => e.StartsWith('.') ? e : "." + e).ToArray(); break;
                    case "--codepage": codePage = int.Parse(Next()); break;
                    case "-h": case "--help": case "/?":
                        Console.WriteLine(Usage);
                        return 0;
                    default: throw new ArgumentException($"Bilinmeyen parametre: {a}");
                }
            }
            if (flags == null || src == null) throw new ArgumentException("--flags ve --src zorunludur");
            if (!dryRun && outDir == null) throw new ArgumentException("--out veya --dry-run belirtilmeli");
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            Console.Error.WriteLine("Hata: " + ex.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(Usage);
            return 1;
        }

        try
        {
            var options = new ProcessOptions
            {
                FlagsPath = flags,
                SourceDir = src,
                OutputDir = outDir,
                DryRun = dryRun,
                CopyOtherFiles = !noCopy,
                CodePage = codePage,
                FlagOptions = new FlagReaderOptions { Mode = mode, InitProcedures = initProcs, UnassignedVariableIsFalse = !unassignedUnknown },
                Extensions = exts ?? new ProcessOptions { FlagsPath = "", SourceDir = "" }.Extensions,
            };
            var report = ProjectProcessor.Run(options, Console.WriteLine);
            var text = report.ToText();
            Console.WriteLine();
            Console.WriteLine(text);
            if (reportPath != null)
            {
                File.WriteAllText(reportPath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                Console.WriteLine($"Rapor yazıldı: {Path.GetFullPath(reportPath)}");
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Hata: " + ex.Message);
            return 2;
        }
    }

    private static string[] SplitList(string s) => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
