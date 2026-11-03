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
          --init-proc <adlar>    Değişken bayraklara değer veren yordamlar, exe açılırken ÇAĞRILDIKLARI SIRAYLA, virgülle
                                 (ör. XFonksiyonu,InitFlags). Aynı bayrakta son atama geçerli olur.
                                 Varsayılan: modüldeki tüm yordamlar; birbirini çağırmayan yordamlar aynı bayrağa farklı
                                 değer veriyorsa bayrak Bilinmiyor sayılır. Yordamların projede çağrıldığı yerler raporda.
          --unassigned-unknown   Hiç atanmamış Boolean değişken bayrağını False değil Bilinmiyor say.
          --define <sabitler>    #If için derleme sabitleri, ör. DEBUG_MODE=0,YENI=1. .vbp'deki CondComp değerlerini ezer
                                 (dosya içindeki #Const satırları o dosyada yine geçerlidir).
          --no-directives        #If gruplarını çözme; #If içindeki bayrak tanımları/atamaları Bilinmiyor sayılır.
          --undefined-unknown    Hiçbir yerde tanımlanmamış #If sabitini 0 sayma (VB'nin davranışı); o grubu olduğu gibi bırak.
          --ext <uzantılar>      İşlenecek uzantılar, virgülle. Varsayılan: .frm,.bas,.cls,.ctl,.dob,.pag
          --keep <dosyalar>      Dönüştürülmeden olduğu gibi aktarılacak dosyalar, virgülle; göreli yol veya dosya adı,
                                 * ve ? kullanılabilir (ör. frmEski.frm,Moduller\*.bas). Tekrar verilebilir.
          --only <dosyalar>      Yalnızca bu dosyaları temizle, diğerlerini olduğu gibi aktar (--keep ile aynı desenler).
          --remove-unused-forms  Projede hiçbir yerde kullanılmayan formları çıktıdaki .vbp'den çıkar (.frm/.frx kopyalanmaz).
          --lib <klasör>         İsteğe bağlı referans klasörü (ör. ortak Lib); form adları burada da aranır. Salt okunur:
                                 dosyalarına asla dokunulmaz. Tekrar verilebilir.
          --no-copy              Kaynak olmayan dosyaları (.frx, .vbp, ...) çıktıya kopyalama.
          --codepage <n>         Kaynak kod sayfası. Varsayılan: 1254
          -h, --help             Bu yardımı göster.

        Çıkış kodu: 0 başarılı, 1 hatalı parametre, 2 çalışma hatası.
        """;

    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { /* yönlendirilmiş çıktı */ }

        string? flags = null, src = null, outDir = null, reportPath = null, defines = null;
        bool dryRun = false, removeUnusedForms = false, noCopy = false, unassignedUnknown = false, noDirectives = false, undefinedUnknown = false;
        var mode = FlagReadMode.Auto;
        string[]? initProcs = null;
        string[]? exts = null;
        var keep = new List<string>();
        var libs = new List<string>();
        List<string>? only = null;
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
                    case "--remove-unused-forms": removeUnusedForms = true; break;
                    case "--lib": libs.Add(Next()); break;
                    case "--no-copy": noCopy = true; break;
                    case "--unassigned-unknown": unassignedUnknown = true; break;
                    case "--define": defines = defines == null ? Next() : defines + "," + Next(); break;
                    case "--no-directives": noDirectives = true; break;
                    case "--undefined-unknown": undefinedUnknown = true; break;
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
                    case "--keep": keep.AddRange(SplitList(Next())); break;
                    case "--only": (only ??= []).AddRange(SplitList(Next())); break;
                    case "--codepage":
                        var cp = Next();
                        if (!int.TryParse(cp, out codePage)) throw new ArgumentException($"Geçersiz --codepage: {cp}");
                        try { VbSourceFile.GetEncoding(codePage); }
                        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
                        {
                            throw new ArgumentException($"Desteklenmeyen kod sayfası: {cp} (Türkçe için 1254)");
                        }
                        break;
                    case "-h": case "--help": case "/?":
                        Console.WriteLine(Usage);
                        return 0;
                    default: throw new ArgumentException($"Bilinmeyen parametre: {a}");
                }
            }
            if (flags == null || src == null) throw new ArgumentException("--flags ve --src zorunludur");
            if (!dryRun && outDir == null) throw new ArgumentException("--out veya --dry-run belirtilmeli");
            if (defines != null && CondConstants.InvalidEntries(defines) is { Count: > 0 } bad)
                throw new ArgumentException($"Geçersiz --define: {string.Join(", ", bad)} (AD=değer biçiminde olmalı, ör. DEBUG_MODE=1)");
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
                Defines = defines,
                ResolveDirectives = !noDirectives,
                FlagOptions = new FlagReaderOptions { Mode = mode, InitProcedures = initProcs, UnassignedVariableIsFalse = !unassignedUnknown },
                Extensions = exts ?? new ProcessOptions { FlagsPath = "", SourceDir = "" }.Extensions,
                KeepAsIs = keep,
                ProcessOnly = only,
                RemoveUnusedForms = removeUnusedForms,
                ReferenceDirs = libs,
                UndefinedConstantsAreZero = !undefinedUnknown,
            };
            // rapor da orijinal dosyaların üzerine yazılamaz; dönüşümden önce kontrol et
            if (reportPath != null) new WriteGuard(Path.GetFullPath(src), Path.GetFullPath(flags), libs).CheckFile(reportPath);
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
