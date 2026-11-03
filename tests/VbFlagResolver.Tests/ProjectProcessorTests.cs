using VbFlagResolver.Core;

namespace VbFlagResolver.Tests;

public class ProjectProcessorTests
{
    private static ProcessOptions Options(string? outDir, bool dryRun = false) => new()
    {
        FlagsPath = Path.Combine(TestPaths.SamplesDir, "mDevelopmentFlags.bas"),
        SourceDir = TestPaths.SamplesDir,
        OutputDir = outDir,
        DryRun = dryRun,
    };

    [Fact]
    public void Processes_samples_into_separate_folder()
    {
        var outDir = TestPaths.NewTempDir();
        var before = Directory.GetFiles(TestPaths.SamplesDir).ToDictionary(f => f, File.ReadAllBytes);

        var report = ProjectProcessor.Run(Options(outDir));

        // orijinaller değişmedi
        foreach (var (f, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(f));

        // tüm dosyalar çıktıda; değişmeyenler bayt düzeyinde aynı
        foreach (var f in before.Keys) Assert.True(File.Exists(Path.Combine(outDir, Path.GetFileName(f))), f);
        foreach (var name in new[] { "frmRapor.frm", "frmMain.frx", "Proje.vbp", "mDevelopmentFlags.bas" })
            Assert.Equal(File.ReadAllBytes(Path.Combine(TestPaths.SamplesDir, name)), File.ReadAllBytes(Path.Combine(outDir, name)));

        // bayrağa atama Bilinmiyor yapıldı ve raporlandı
        Assert.False(report.Flags.TryGetKnown("gbFlagCalismaZamani", out _));
        Assert.Contains(report.FlagAssignments, a => a.File == "frmAyarlar.frm");

        var main = report.Files.Single(f => f.RelativePath == "frmMain.frm");
        Assert.True(main.Changed);
        Assert.Contains(main.Warnings, w => w.Message.Contains("'Atla'"));
        Assert.Contains(main.Warnings, w => w.Message.Contains("KaydetVeKapat()"));

        var text = File.ReadAllText(Path.Combine(outDir, "frmMain.frm"), VbSourceFile.GetEncoding(1254));
        Assert.DoesNotContain("FLAG_ESKI_MENU", text);
        Assert.Contains("    If gbFlagCalismaZamani Then\r\n        frmStokYeni.Show vbModal\r\n    Else\r\n        frmStok.Show vbModal\r\n    End If", text);
        Assert.Contains("ğüşıöç", text);

        Assert.Contains("Okunan bayrak : 41", report.ToText());

        // karmaşık örnekler: alt klasör, sınıf modülü, parametre gölgelemesi
        Assert.True(File.Exists(Path.Combine(outDir, "Moduller", "modRapor.bas")));
        Assert.False(report.Flags.TryGetKnown("FLAG_GOLGE", out _));
        Assert.True(report.Flags.TryGetKnown("FLAG_DEBUG_ARAYUZ", out var dbg) && !dbg); // .vbp: DEBUG_MODE = 0 → #Else
        var enc = VbSourceFile.GetEncoding(1254);
        var siparis = File.ReadAllText(Path.Combine(outDir, "frmSiparis.frm"), enc);
        Assert.Contains("    If gbFlagKomutSatiri Then\r\n        txtMusteri.Text = Command$\r\n    Else\r\n        txtMusteri.Text = \"log\"\r\n    End If", siparis);
        Assert.Contains("    Call Hazirla: Debug.Print \"hazır\"\r\n", siparis);
        Assert.Contains("    GoTo 100\r\n", siparis);
        Assert.Contains("    If SEVIYE = True Then Debug.Print \"seviye\"", siparis);
        Assert.Contains("    Else If gbFlagSonradanAcik Then\r\n", siparis);  // "Else If" bloğu olduğu gibi
        Assert.Contains("    If FLAG_GOLGE Then\r\n", siparis);
        Assert.Contains("\tDebug.Print \"stok\"\r\n", siparis);
        var cls = File.ReadAllText(Path.Combine(outDir, "clsYazici.cls"), enc);
        Assert.StartsWith("VERSION 1.0 CLASS\r\nBEGIN\r\n", cls);
        Assert.Contains("    Tip = ytLazer\r\n", cls);
        var sip = report.Files.Single(f => f.RelativePath == "frmSiparis.frm");
        Assert.Contains(sip.Warnings, w => w.Message.Contains("EskiKontrol()"));
        Assert.Contains(sip.Warnings, w => w.Message.Contains("'YeniDene'"));
    }

    [Fact]
    public void Reports_where_flag_procedures_are_called()
    {
        var src = TestPaths.NewTempDir();
        File.WriteAllText(Path.Combine(src, "mFlags.bas"), """
            Attribute VB_Name = "mFlags"
            Public gbA As Boolean
            Public Sub InitFlags()
                gbA = True
            End Sub
            Public Sub XFonksiyonu()
                gbA = False
            End Sub
            """);
        File.WriteAllText(Path.Combine(src, "modMain.bas"), """
            Attribute VB_Name = "modMain"
            Sub Main()
                Call mFlags.XFonksiyonu
                InitFlags
                If Command$ <> "" Then XFonksiyonu
            End Sub
            """);
        var report = ProjectProcessor.Run(new ProcessOptions { FlagsPath = Path.Combine(src, "mFlags.bas"), SourceDir = src, DryRun = true });

        Assert.Equal(["XFonksiyonu", "InitFlags", "XFonksiyonu"], report.ProcedureCalls.Select(c => c.Procedure));
        Assert.All(report.ProcedureCalls, c => Assert.Equal("Sub Main", c.Caller));
        Assert.Equal([false, false, true], report.ProcedureCalls.Select(c => c.Conditional));
        var text = report.ToText();
        Assert.Contains("modMain.bas, Sub Main: XFonksiyonu (satır 3) → InitFlags (satır 4) → XFonksiyonu (satır 5, koşullu)", text);

        // koşulsuz çağrı sırasından öneri
        Assert.Equal(["XFonksiyonu", "InitFlags"], report.SuggestInitOrder()!.Value.Order);
        Assert.Contains("Önerilen çağrı sırası: XFonksiyonu,InitFlags", text);
    }

    [Theory]
    [InlineData(null, "    a = 1\r\n    If x Then\r\n        b = 1\r\n    End If\r\n")]       // .vbp: DEBUG_MODE = 0
    [InlineData("DEBUG_MODE=1", "    gbX = False\r\n    If gbX Then\r\n        a = 1\r\n    End If\r\n    b = 1\r\n")] // kullanıcı ezer; atama derlenir → gbX Bilinmiyor
    public void Uses_vbp_condcomp_and_user_defines(string? defines, string expectedBody)
    {
        var src = TestPaths.NewTempDir();
        File.WriteAllText(Path.Combine(src, "Proje.vbp"), "Type=Exe\r\nCondComp=\"DEBUG_MODE = 0\"\r\n");
        File.WriteAllText(Path.Combine(src, "mFlags.bas"), "Attribute VB_Name = \"mFlags\"\r\nPublic Const T1 = True\r\nPublic gbX As Boolean\r\nSub InitFlags()\r\n    gbX = True\r\nEnd Sub\r\n");
        File.WriteAllText(Path.Combine(src, "modA.bas"),
            "Sub A()\r\n" +
            "#If DEBUG_MODE Then\r\n    gbX = False\r\n#End If\r\n" +              // derlenmeyen dalda atama
            "    If gbX Then\r\n        a = 1\r\n    End If\r\n" +
            "#If DEBUG_MODE Then\r\n    If T1 Then\r\n#Else\r\n    If T1 And x Then\r\n#End If\r\n" +
            "        b = 1\r\n    End If\r\n" +
            "End Sub\r\n");
        var outDir = TestPaths.NewTempDir();
        var report = ProjectProcessor.Run(new ProcessOptions
        {
            FlagsPath = Path.Combine(src, "mFlags.bas"), SourceDir = src, OutputDir = outDir, Defines = defines,
        });

        var text = File.ReadAllText(Path.Combine(outDir, "modA.bas"));
        Assert.Equal("Sub A()\r\n" + expectedBody + "End Sub\r\n", text);
        Assert.Equal(defines == null ? 0 : 1, report.Constants.All.Single().Value!.Value.Number);
        Assert.Contains("Koşullu derleme sabitleri: 1", report.ToText());
    }

    [Fact]
    public void Dry_run_writes_nothing()
    {
        var outDir = Path.Combine(TestPaths.NewTempDir(), "out");
        var report = ProjectProcessor.Run(Options(outDir, dryRun: true));
        Assert.False(Directory.Exists(outDir));
        Assert.Contains(report.Files, f => f.Changed);
        Assert.Contains("kuru çalıştırma", report.ToText());
    }

    [Fact]
    public void Refuses_to_write_into_source_folder()
    {
        Assert.Throws<ArgumentException>(() => ProjectProcessor.Run(Options(TestPaths.SamplesDir)));
        Assert.Throws<ArgumentException>(() => ProjectProcessor.Run(Options(Path.GetDirectoryName(TestPaths.SamplesDir))));
    }

    [Theory]
    [InlineData("Sub X(ByVal T1 As Boolean)\r\n", false)]                   // parametre gölgeler
    [InlineData("Private Function X(a As Long, Optional T1 = 0) As Long\r\n", false)]
    [InlineData("Sub X()\r\n    For T1 = 0 To 1\r\n    Next\r\n", false)]  // döngü değişkeni
    [InlineData("Sub X()\r\n    For Each T1 In c\r\n    Next\r\n", false)]
    [InlineData("Public Declare Function Y Lib \"k\" (ByVal T1 As Long) As Long\r\nSub X()\r\n", true)] // Declare parametresi gölgelemez
    public void Parameter_or_loop_variable_named_like_flag_makes_it_unknown(string head, bool known)
    {
        var src = TestPaths.NewTempDir();
        File.WriteAllText(Path.Combine(src, "mFlags.bas"), "Attribute VB_Name = \"mFlags\"\r\nPublic Const T1 = True\r\n");
        File.WriteAllText(Path.Combine(src, "frmA.frm"), head + "    If T1 Then\r\n        a = 1\r\n    End If\r\nEnd Sub\r\n");
        var report = ProjectProcessor.Run(new ProcessOptions { FlagsPath = Path.Combine(src, "mFlags.bas"), SourceDir = src, DryRun = true });
        Assert.Equal(known, report.Flags.TryGetKnown("T1", out _));
        Assert.Equal(known, report.Files.Single().Changed);
    }

    [Fact]
    public void Assignment_after_procedure_header_colon_is_detected()
    {
        var src = TestPaths.NewTempDir();
        File.WriteAllText(Path.Combine(src, "mFlags.bas"), "Attribute VB_Name = \"mFlags\"\r\nPublic gbV As Boolean\r\n");
        File.WriteAllText(Path.Combine(src, "frmA.frm"), "Private Sub Form_Load(): gbV = True: End Sub\r\n");
        var report = ProjectProcessor.Run(new ProcessOptions { FlagsPath = Path.Combine(src, "mFlags.bas"), SourceDir = src, DryRun = true });
        Assert.False(report.Flags.TryGetKnown("gbV", out _));
    }

    [Fact]
    public void Private_flag_used_from_other_module_is_unknown()
    {
        var src = TestPaths.NewTempDir();
        File.WriteAllText(Path.Combine(src, "mFlags.bas"),
            "Attribute VB_Name = \"mFlags\"\r\nPrivate Const P1 = True\r\nConst P2 = True\r\nPublic Const P3 = True\r\n");
        File.WriteAllText(Path.Combine(src, "frmA.frm"), "Sub X()\r\n    If P1 Or P2 Or P3 Then\r\n        a = 1\r\n    End If\r\nEnd Sub\r\n");
        var report = ProjectProcessor.Run(new ProcessOptions { FlagsPath = Path.Combine(src, "mFlags.bas"), SourceDir = src, DryRun = true });
        Assert.False(report.Flags.TryGetKnown("P1", out _));
        Assert.False(report.Flags.TryGetKnown("P2", out _));
        Assert.True(report.Flags.TryGetKnown("P3", out _));
        Assert.Contains(report.FlagAssignments, a => a.Message.Contains("erişilemez"));
    }

    [Fact]
    public void Uncalled_init_procedure_is_reported()
    {
        var src = TestPaths.NewTempDir();
        File.WriteAllText(Path.Combine(src, "mFlags.bas"), "Attribute VB_Name = \"mFlags\"\r\nPublic gbA As Boolean\r\nSub InitFlags()\r\n    gbA = True\r\nEnd Sub\r\n");
        File.WriteAllText(Path.Combine(src, "modA.bas"), "Sub Main()\r\nEnd Sub\r\n");
        var report = ProjectProcessor.Run(new ProcessOptions { FlagsPath = Path.Combine(src, "mFlags.bas"), SourceDir = src, DryRun = true });
        Assert.Contains(report.FlagNotes, n => n.Message.Contains("InitFlags çalıştırılmış sayıldı ama projede çağrıldığı yer bulunamadı"));
    }

    [Theory]
    [InlineData("frmSiparis.frm", "frmSiparis.frm")]
    [InlineData("FRMSIPARIS.FRM", "frmSiparis.frm")]               // büyük/küçük harf duyarsız
    [InlineData("Moduller\\*.bas", "Moduller\\modRapor.bas")]       // klasör + joker
    [InlineData("modRapor.bas", "Moduller\\modRapor.bas")]          // yalnızca dosya adı
    [InlineData("frm*.frm", "frmSiparis.frm")]
    public void Keep_patterns_match(string pattern, string rel)
        => Assert.True(ProcessOptions.MatchesKeep(rel, [pattern]));

    [Theory]
    [InlineData("frmSiparis.frm", "Moduller\\frmSiparis2.frm")]
    [InlineData("Moduller\\*.bas", "modGenel.bas")]
    public void Keep_patterns_do_not_match(string pattern, string rel)
        => Assert.False(ProcessOptions.MatchesKeep(rel, [pattern]));

    [Fact]
    public void Kept_files_are_copied_byte_identical_but_still_scanned()
    {
        var outDir = TestPaths.NewTempDir();
        var o = Options(outDir);
        var report = ProjectProcessor.Run(new ProcessOptions
        {
            FlagsPath = o.FlagsPath, SourceDir = o.SourceDir, OutputDir = outDir, KeepAsIs = ["frmSiparis.frm", "Moduller\\*.bas"],
        });
        foreach (var rel in new[] { "frmSiparis.frm", Path.Combine("Moduller", "modRapor.bas") })
            Assert.Equal(File.ReadAllBytes(Path.Combine(TestPaths.SamplesDir, rel)), File.ReadAllBytes(Path.Combine(outDir, rel)));
        var sip = report.Files.Single(f => f.RelativePath == "frmSiparis.frm");
        Assert.True(sip.KeptAsIs);
        Assert.False(sip.Changed);
        // işaretli dosyadaki parametre gölgelemesi yine taranır
        Assert.False(report.Flags.TryGetKnown("FLAG_GOLGE", out _));
        Assert.Contains("olduğu gibi aktarılan: 2", report.ToText());
        Assert.True(report.Files.Single(f => f.RelativePath == "frmMain.frm").Changed);
    }

    [Fact]
    public void Process_only_cleans_selected_files_and_keeps_the_rest()
    {
        var outDir = TestPaths.NewTempDir();
        var o = Options(outDir);
        var report = ProjectProcessor.Run(new ProcessOptions
        {
            FlagsPath = o.FlagsPath, SourceDir = o.SourceDir, OutputDir = outDir, ProcessOnly = ["frmMain.frm"],
        });
        Assert.True(report.Files.Single(f => f.RelativePath == "frmMain.frm").Changed);
        foreach (var f in report.Files.Where(f => f.RelativePath != "frmMain.frm"))
        {
            Assert.True(f.KeptAsIs, f.RelativePath);
            Assert.Equal(File.ReadAllBytes(Path.Combine(TestPaths.SamplesDir, f.RelativePath)), File.ReadAllBytes(Path.Combine(outDir, f.RelativePath)));
        }

        // boş liste: hiçbir dosya temizlenmez
        var none = ProjectProcessor.Run(new ProcessOptions { FlagsPath = o.FlagsPath, SourceDir = o.SourceDir, DryRun = true, ProcessOnly = [] });
        Assert.All(none.Files, f => Assert.True(f.KeptAsIs));
    }

    [Theory]
    [InlineData(true, "Sub A()\r\n    a = 2\r\nEnd Sub\r\n")]   // VB: tanımsız ARSLAN = 0 → #Else
    [InlineData(false, "Sub A()\r\n#If ARSLAN = 1 Then\r\n    a = 1\r\n#Else\r\n    a = 2\r\n#End If\r\nEnd Sub\r\n")]
    public void Undefined_directive_constant_is_zero_by_default(bool zero, string expected)
    {
        var src = TestPaths.NewTempDir();
        File.WriteAllText(Path.Combine(src, "mFlags.bas"), "Attribute VB_Name = \"mFlags\"\r\nPublic Const T1 = True\r\n");
        File.WriteAllText(Path.Combine(src, "modA.bas"), "Sub A()\r\n#If ARSLAN = 1 Then\r\n    a = 1\r\n#Else\r\n    a = 2\r\n#End If\r\nEnd Sub\r\n");
        var outDir = TestPaths.NewTempDir();
        var report = ProjectProcessor.Run(new ProcessOptions
        {
            FlagsPath = Path.Combine(src, "mFlags.bas"), SourceDir = src, OutputDir = outDir, UndefinedConstantsAreZero = zero,
        });
        Assert.Equal(expected, File.ReadAllText(Path.Combine(outDir, "modA.bas")));
        if (zero) Assert.Contains("tanımsız sabit 0 sayıldı: ARSLAN", report.ToText());
    }

    // ---- kullanılmayan formları çıkarma ----

    private const string UnusedFlags = "Attribute VB_Name = \"mFlags\"\r\nPublic Const T1 = True\r\nPublic Const F1 = False\r\n";
    private const string UnusedVbp = "Type=Exe\r\nForm=frmMain.frm\r\nForm=frmEski.frm\r\nModule=mFlags; mFlags.bas\r\nStartup=\"frmMain\"\r\n";

    private static string FormFile(string name, string code, string kind = "Form", string extra = "")
        => $"VERSION 5.00\r\nBegin VB.{kind} {name} \r\n   Caption = \"{name}\"\r\n{extra}End\r\nAttribute VB_Name = \"{name}\"\r\n" + code;

    /// <summary>Geçici proje kurar; (src, out) döner.</summary>
    private static (string Src, string Out) UnusedProject(string vbp, params (string Name, string Content)[] files)
    {
        var root = TestPaths.NewTempDir();
        var src = Path.Combine(root, "src");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "mFlags.bas"), UnusedFlags);
        File.WriteAllText(Path.Combine(src, "Proje.vbp"), vbp);
        foreach (var (name, content) in files) File.WriteAllText(Path.Combine(src, name), content);
        return (src, Path.Combine(root, "out"));
    }

    private static ProcessOptions UnusedOptions(string src, string? outDir, bool remove = true, bool dryRun = false,
        IReadOnlyCollection<string>? usage = null, IReadOnlyCollection<string>? only = null) => new()
    {
        FlagsPath = Path.Combine(src, "mFlags.bas"), SourceDir = src, OutputDir = outDir, DryRun = dryRun, RemoveUnusedForms = remove, UsageSelection = usage, ProcessOnly = only,
    };

    private const string MainWithDeadRef = "Sub A()\r\n    If F1 Then\r\n        frmEski.Show\r\n    End If\r\nEnd Sub\r\n";

    /// <summary>Kaynak klasörün dışında (kardeş) bir Lib klasörü kurar.</summary>
    private static string LibDir(string src, params (string Name, string Content)[] files)
    {
        var lib = Path.Combine(Path.GetDirectoryName(src)!, "Lib");
        Directory.CreateDirectory(Path.Combine(lib, "Alt"));
        foreach (var (name, content) in files) File.WriteAllText(Path.Combine(lib, name), content);
        return lib;
    }

    [Fact]
    public void Form_used_only_from_reference_lib_is_kept_and_lib_is_untouched()
    {
        var (src, outDir) = UnusedProject(UnusedVbp, ("frmMain.frm", FormFile("frmMain", "")), ("frmEski.frm", FormFile("frmEski", "")));
        var lib = LibDir(src,
            ("modOrtak.bas", "Attribute VB_Name = \"modOrtak\"\r\nSub Ac()\r\n    If F1 Then frmEski.Show\r\nEnd Sub\r\n"),
            (Path.Combine("Alt", "notlar.bas"), "Attribute VB_Name = \"notlar\"\r\n' frmMain yalnızca yorumda\r\n"));
        var before = Directory.GetFiles(lib, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllBytes);

        var o = UnusedOptions(src, outDir);
        var report = ProjectProcessor.Run(new ProcessOptions
        {
            FlagsPath = o.FlagsPath, SourceDir = src, OutputDir = outDir, RemoveUnusedForms = true, ReferenceDirs = [lib],
        });

        // Lib dönüştürülmez: içindeki "If F1 Then frmEski.Show" olduğu gibi sayılır, form kalır
        Assert.Empty(report.RemovedForms);
        var kept = report.KeptForms.Single(k => k.Name == "frmEski");
        Assert.Contains("modOrtak.bas satır 3", kept.Reason);   // nerede kullanıldığı
        Assert.Contains("KULLANILAN FORMLAR", report.ToText());
        Assert.True(File.Exists(Path.Combine(outDir, "frmEski.frm")));
        Assert.Equal(2, report.ReferenceFileCount);
        Assert.Contains("salt okunur", report.ToText());
        // Lib dosyaları değişmedi ve çıktıya kopyalanmadı
        foreach (var (f, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(f));
        Assert.False(File.Exists(Path.Combine(outDir, "modOrtak.bas")));
    }

    [Fact]
    public void Kept_forms_report_where_they_are_used()
    {
        var vbp = "Type=Exe\r\nForm=frmMain.frm\r\nForm=frmA.frm\r\nForm=frmStr.frm\r\nStartup=\"frmMain\"\r\n";
        var (src, outDir) = UnusedProject(vbp,
            ("frmMain.frm", FormFile("frmMain", "Sub X()\r\n    If F1 Then y = 1\r\n    frmA.Show\r\n    s = \"frmStr\"\r\nEnd Sub\r\n")),
            ("frmA.frm", FormFile("frmA", "")), ("frmStr.frm", FormFile("frmStr", "")), ("frmLoose.frm", FormFile("frmLoose", "")));
        File.AppendAllText(Path.Combine(src, "mFlags.bas"), "Sub Ac()\r\n    frmA.Show\r\nEnd Sub\r\n");
        var report = ProjectProcessor.Run(UnusedOptions(src, outDir));
        string Reason(string n) => report.KeptForms.Single(k => k.Name == n).Reason;
        Assert.Contains("başlangıç formu", Reason("frmMain"));
        // önce form olmayan modül gösterilir; frmMain temizlendiği için satır numarası çıktıdaki satırdır
        Assert.Contains("mFlags.bas satır", Reason("frmA"));
        Assert.Contains("+1 dosyada daha", Reason("frmA"));
        Assert.Contains("string içinde", Reason("frmStr"));
        Assert.Contains(report.RemovedForms, r => r.Name == "frmLoose" && r.Reason.Contains("listelenmiyor"));
    }

    [Fact]
    public void Without_reference_lib_the_same_form_is_removed()
    {
        var (src, _) = UnusedProject(UnusedVbp, ("frmMain.frm", FormFile("frmMain", "")), ("frmEski.frm", FormFile("frmEski", "")));
        LibDir(src, ("modOrtak.bas", "Attribute VB_Name = \"modOrtak\"\r\nSub Ac()\r\n    frmEski.Show\r\nEnd Sub\r\n"));
        var report = ProjectProcessor.Run(UnusedOptions(src, null, dryRun: true)); // Lib verilmedi
        Assert.Equal(["frmEski.frm"], report.RemovedForms.Select(r => r.RelativePath));
    }

    [Fact]
    public void Reference_lib_must_exist_and_output_cannot_be_inside_it()
    {
        var (src, _) = UnusedProject(UnusedVbp, ("frmMain.frm", FormFile("frmMain", "")));
        var lib = LibDir(src);
        Assert.Throws<DirectoryNotFoundException>(() => ProjectProcessor.Run(new ProcessOptions
        {
            FlagsPath = Path.Combine(src, "mFlags.bas"), SourceDir = src, DryRun = true, ReferenceDirs = [lib + "_yok"],
        }));
        Assert.Throws<ArgumentException>(() => ProjectProcessor.Run(new ProcessOptions
        {
            FlagsPath = Path.Combine(src, "mFlags.bas"), SourceDir = src, OutputDir = Path.Combine(lib, "cikti"), ReferenceDirs = [lib],
        }));
        Assert.Throws<InvalidOperationException>(() =>
            new WriteGuard(src, Path.Combine(src, "mFlags.bas"), [lib]).CheckFile(Path.Combine(lib, "rapor.txt")));
    }

    [Fact]
    public void Form_used_from_file_outside_ext_filter_is_kept()
    {
        var (src, _) = UnusedProject(UnusedVbp, ("frmMain.frm", FormFile("frmMain", "")), ("frmEski.frm", FormFile("frmEski", "")));
        File.WriteAllText(Path.Combine(src, "modA.bas"), "Attribute VB_Name = \"modA\"\r\nSub X()\r\n    frmEski.Show\r\nEnd Sub\r\n");
        var o = UnusedOptions(src, null, dryRun: true);
        var report = ProjectProcessor.Run(new ProcessOptions
        {
            FlagsPath = o.FlagsPath, SourceDir = src, DryRun = true, RemoveUnusedForms = true, Extensions = [".frm"],
        });
        Assert.Empty(report.RemovedForms);
    }

    [Fact]
    public void Form_not_listed_in_vbp_or_kept_as_is_is_not_removed()
    {
        var (src, _) = UnusedProject("Type=Exe\r\nForm=frmMain.frm\r\nForm=frmKeep.frm\r\nStartup=\"frmMain\"\r\n",
            ("frmMain.frm", FormFile("frmMain", "")), ("frmLoose.frm", FormFile("frmLoose", "")), ("frmKeep.frm", FormFile("frmKeep", "")));
        var report = ProjectProcessor.Run(new ProcessOptions
        {
            FlagsPath = Path.Combine(src, "mFlags.bas"), SourceDir = src, DryRun = true, RemoveUnusedForms = true, KeepAsIs = ["frmKeep.frm"],
        });
        // frmLoose .vbp'de yok: projede tanımlı değil, çıkarılır; frmKeep olduğu gibi aktarılıyor, kalır
        Assert.Equal(["frmLoose.frm"], report.RemovedForms.Select(r => r.RelativePath));
        Assert.Contains("listelenmiyor", report.RemovedForms[0].Reason);
    }

    [Fact]
    public void Cr_only_vbp_is_parsed_and_stripped()
    {
        var vbp = "Type=Exe\rForm=frmMain.frm\rForm=frmEski.frm\rStartup=\"frmMain\"\r";
        var (src, outDir) = UnusedProject(vbp, ("frmMain.frm", FormFile("frmMain", "")), ("frmEski.frm", FormFile("frmEski", "")));
        var report = ProjectProcessor.Run(UnusedOptions(src, outDir));
        Assert.Equal(["frmEski.frm"], report.RemovedForms.Select(r => r.RelativePath));
        Assert.Equal("Type=Exe\rForm=frmMain.frm\rStartup=\"frmMain\"\r", File.ReadAllText(Path.Combine(outDir, "Proje.vbp")));
    }

    [Theory]
    [InlineData(".\\modA.bas", "modA.bas", true)]
    [InlineData(".\\modA.bas", "Eski\\modA.bas", false)]   // arayüz seçimi: tam göreli yol
    [InlineData("modA.bas", "Eski\\modA.bas", true)]       // komut satırı: yalnızca dosya adı
    [InlineData(".\\.gizli.bas", ".gizli.bas", true)]      // nokta ile başlayan ad
    public void Rooted_patterns_match_full_relative_path(string pattern, string rel, bool expected)
        => Assert.Equal(expected, ProcessOptions.MatchesKeep(rel, [pattern]));

    [Fact]
    public void Unused_forms_referencing_each_other_are_both_removed()
    {
        var (src, _) = UnusedProject("Type=Exe\r\nForm=frmMain.frm\r\nForm=frmA.frm\r\nForm=frmB.frm\r\nStartup=\"frmMain\"\r\n",
            ("frmMain.frm", FormFile("frmMain", "")),
            ("frmA.frm", FormFile("frmA", "Sub X()\r\n    frmB.Show\r\nEnd Sub\r\n")),
            ("frmB.frm", FormFile("frmB", "Sub Y()\r\n    frmA.Show\r\nEnd Sub\r\n")));
        var report = ProjectProcessor.Run(UnusedOptions(src, null, dryRun: true));
        Assert.Equal(["frmA.frm", "frmB.frm"], report.RemovedForms.Select(r => r.RelativePath).Order());
    }

    [Fact]
    public void Quoted_form_line_is_removed_from_vbp()
    {
        var vbp = "Type=Exe\r\nForm=frmMain.frm\r\nForm=\"frmEski.frm\"\r\nStartup=\"frmMain\"\r\n";
        var (src, outDir) = UnusedProject(vbp, ("frmMain.frm", FormFile("frmMain", "")), ("frmEski.frm", FormFile("frmEski", "")));
        ProjectProcessor.Run(UnusedOptions(src, outDir));
        Assert.Equal("Type=Exe\r\nForm=frmMain.frm\r\nStartup=\"frmMain\"\r\n", File.ReadAllText(Path.Combine(outDir, "Proje.vbp")));
    }

    [Fact]
    public void Form_opened_from_flags_module_is_kept()
    {
        var (src, _) = UnusedProject(UnusedVbp, ("frmMain.frm", FormFile("frmMain", "")), ("frmEski.frm", FormFile("frmEski", "")));
        File.AppendAllText(Path.Combine(src, "mFlags.bas"), "Sub InitFlags()\r\n    frmEski.Show\r\nEnd Sub\r\n");
        var report = ProjectProcessor.Run(UnusedOptions(src, null, dryRun: true));
        Assert.Empty(report.RemovedForms);
    }

    [Fact]
    public void Without_vbp_no_form_is_removed()
    {
        var (src, _) = UnusedProject(UnusedVbp, ("frmMain.frm", FormFile("frmMain", "")), ("frmEski.frm", FormFile("frmEski", "")));
        File.Delete(Path.Combine(src, "Proje.vbp"));
        var report = ProjectProcessor.Run(UnusedOptions(src, null, dryRun: true));
        Assert.Empty(report.RemovedForms);
        Assert.Contains(report.FormNotes, n => n.Message.Contains(".vbp bulunamadı"));
    }

    [Fact]
    public void Unused_form_referenced_only_in_removed_block_is_removed()
    {
        var (src, outDir) = UnusedProject(UnusedVbp,
            ("frmMain.frm", FormFile("frmMain", MainWithDeadRef)),
            ("frmEski.frm", FormFile("frmEski", "")));
        File.WriteAllBytes(Path.Combine(src, "frmEski.frx"), new byte[] { 1, 2, 3 });

        var report = ProjectProcessor.Run(UnusedOptions(src, outDir));

        Assert.Contains(report.RemovedForms, r => r.RelativePath == "frmEski.frm");
        Assert.True(report.Files.Single(f => f.RelativePath == "frmEski.frm").RemovedFromProject);
        Assert.False(File.Exists(Path.Combine(outDir, "frmEski.frm")));
        Assert.False(File.Exists(Path.Combine(outDir, "frmEski.frx")));
        Assert.True(File.Exists(Path.Combine(outDir, "frmMain.frm")));
        Assert.Equal(UnusedVbp.Replace("Form=frmEski.frm\r\n", ""), File.ReadAllText(Path.Combine(outDir, "Proje.vbp")));
        Assert.Contains("KULLANILMAYAN FORMLAR", report.ToText());
    }

    [Fact]
    public void Unused_form_removal_is_off_by_default()
    {
        var (src, outDir) = UnusedProject(UnusedVbp,
            ("frmMain.frm", FormFile("frmMain", MainWithDeadRef)),
            ("frmEski.frm", FormFile("frmEski", "")));

        var report = ProjectProcessor.Run(UnusedOptions(src, outDir, remove: false));

        Assert.Empty(report.RemovedForms);
        Assert.True(File.Exists(Path.Combine(outDir, "frmEski.frm")));
        Assert.Equal(UnusedVbp, File.ReadAllText(Path.Combine(outDir, "Proje.vbp")));
    }

    [Theory]
    [InlineData("Sub A()\r\n    If T1 Then\r\n        frmEski.Show\r\n    End If\r\nEnd Sub\r\n")]
    [InlineData("Sub A()\r\n    frmEski.Show\r\nEnd Sub\r\n")]
    public void Form_referenced_in_live_code_is_kept(string code)
    {
        var (src, outDir) = UnusedProject(UnusedVbp,
            ("frmMain.frm", FormFile("frmMain", code)),
            ("frmEski.frm", FormFile("frmEski", "")));

        var report = ProjectProcessor.Run(UnusedOptions(src, outDir));

        Assert.Empty(report.RemovedForms);
        Assert.True(File.Exists(Path.Combine(outDir, "frmEski.frm")));
    }

    [Fact]
    public void Startup_form_without_references_is_kept()
    {
        var (src, outDir) = UnusedProject("Type=Exe\r\nForm=frmMain.frm\r\nModule=mFlags; mFlags.bas\r\nStartup=\"frmMain\"\r\n",
            ("frmMain.frm", FormFile("frmMain", "Sub A()\r\nEnd Sub\r\n")));

        var report = ProjectProcessor.Run(UnusedOptions(src, outDir));

        Assert.Empty(report.RemovedForms);
        Assert.True(File.Exists(Path.Combine(outDir, "frmMain.frm")));
    }

    [Fact]
    public void Removal_chains_through_removed_forms()
    {
        var vbp = "Type=Exe\r\nForm=frmMain.frm\r\nForm=frmA.frm\r\nForm=frmB.frm\r\nModule=mFlags; mFlags.bas\r\nStartup=\"frmMain\"\r\n";
        var (src, outDir) = UnusedProject(vbp,
            ("frmMain.frm", FormFile("frmMain", "Sub X()\r\n    If F1 Then\r\n        frmA.Show\r\n    End If\r\nEnd Sub\r\n")),
            ("frmA.frm", FormFile("frmA", "Sub Y()\r\n    frmB.Show\r\nEnd Sub\r\n")),
            ("frmB.frm", FormFile("frmB", "")));

        var report = ProjectProcessor.Run(UnusedOptions(src, outDir));

        Assert.Contains(report.RemovedForms, r => r.RelativePath == "frmA.frm");
        Assert.Contains(report.RemovedForms, r => r.RelativePath == "frmB.frm");
        Assert.False(File.Exists(Path.Combine(outDir, "frmA.frm")));
        Assert.False(File.Exists(Path.Combine(outDir, "frmB.frm")));
        Assert.Equal(vbp.Replace("Form=frmA.frm\r\n", "").Replace("Form=frmB.frm\r\n", ""), File.ReadAllText(Path.Combine(outDir, "Proje.vbp")));
    }

    [Fact]
    public void Form_name_inside_string_literal_is_kept_with_note()
    {
        var (src, outDir) = UnusedProject(UnusedVbp,
            ("frmMain.frm", FormFile("frmMain", "Sub A()\r\n    Dim s As String\r\n    s = \"frmEski\"\r\nEnd Sub\r\n")),
            ("frmEski.frm", FormFile("frmEski", "")));

        var report = ProjectProcessor.Run(UnusedOptions(src, outDir));

        Assert.Empty(report.RemovedForms);
        Assert.NotEmpty(report.FormNotes);
        Assert.True(File.Exists(Path.Combine(outDir, "frmEski.frm")));
    }

    [Fact]
    public void Mdi_parent_is_kept_while_child_is_alive()
    {
        var vbp = "Type=Exe\r\nForm=frmMain.frm\r\nForm=frmAna.frm\r\nForm=frmCocuk.frm\r\nModule=mFlags; mFlags.bas\r\nStartup=\"frmMain\"\r\n";
        var (src, outDir) = UnusedProject(vbp,
            ("frmMain.frm", FormFile("frmMain", "Sub A()\r\n    frmCocuk.Show\r\nEnd Sub\r\n")),
            ("frmAna.frm", FormFile("frmAna", "", "MDIForm")),
            ("frmCocuk.frm", FormFile("frmCocuk", "", "Form", "   MDIChild        =   -1  'True\r\n")));

        var report = ProjectProcessor.Run(UnusedOptions(src, outDir));

        Assert.Empty(report.RemovedForms);
        Assert.True(File.Exists(Path.Combine(outDir, "frmAna.frm")));
        Assert.True(File.Exists(Path.Combine(outDir, "frmCocuk.frm")));
    }

    [Fact]
    public void Mdi_parent_and_unused_child_are_both_removed()
    {
        var vbp = "Type=Exe\r\nForm=frmMain.frm\r\nForm=frmAna.frm\r\nForm=frmCocuk.frm\r\nModule=mFlags; mFlags.bas\r\nStartup=\"frmMain\"\r\n";
        var (src, outDir) = UnusedProject(vbp,
            ("frmMain.frm", FormFile("frmMain", "Sub A()\r\nEnd Sub\r\n")),
            ("frmAna.frm", FormFile("frmAna", "", "MDIForm")),
            ("frmCocuk.frm", FormFile("frmCocuk", "", "Form", "   MDIChild        =   -1  'True\r\n")));

        var report = ProjectProcessor.Run(UnusedOptions(src, outDir));

        Assert.Contains(report.RemovedForms, r => r.RelativePath == "frmAna.frm");
        Assert.Contains(report.RemovedForms, r => r.RelativePath == "frmCocuk.frm");
        Assert.False(File.Exists(Path.Combine(outDir, "frmAna.frm")));
        Assert.False(File.Exists(Path.Combine(outDir, "frmCocuk.frm")));
    }

    [Fact]
    public void Dry_run_reports_removed_forms_without_writing()
    {
        var (src, outDir) = UnusedProject(UnusedVbp,
            ("frmMain.frm", FormFile("frmMain", MainWithDeadRef)),
            ("frmEski.frm", FormFile("frmEski", "")));

        var report = ProjectProcessor.Run(UnusedOptions(src, outDir, dryRun: true));

        Assert.Contains(report.RemovedForms, r => r.RelativePath == "frmEski.frm");
        Assert.False(Directory.Exists(outDir));
    }

    [Fact]
    public void Usage_selection_and_process_only_decide_which_code_is_analysed()
    {
        var (src, outDir) = UnusedProject(UnusedVbp,
            ("frmMain.frm", FormFile("frmMain", MainWithDeadRef)),
            ("frmEski.frm", FormFile("frmEski", "")));

        // frmMain seçili değil → özgün kod analiz edilir → frmEski kullanılıyor
        Assert.Empty(ProjectProcessor.Run(UnusedOptions(src, outDir, dryRun: true, usage: new string[0])).RemovedForms);

        // frmMain seçili → bayrak bloğu silinir → frmEski kullanılmıyor
        Assert.Contains(ProjectProcessor.Run(UnusedOptions(src, outDir, dryRun: true, usage: new[] { "frmMain.frm" })).RemovedForms, r => r.RelativePath == "frmEski.frm");

        // gerçek çalıştırma, ProcessOnly boş → kalır
        Assert.Empty(ProjectProcessor.Run(UnusedOptions(src, outDir, only: new string[0])).RemovedForms);
        Assert.True(File.Exists(Path.Combine(outDir, "frmEski.frm")));
    }

    [Fact]
    public void Skip_flags_leave_their_if_blocks_untouched_but_process_inner_blocks()
    {
        var src = TestPaths.NewTempDir();
        File.WriteAllText(Path.Combine(src, "mFlags.bas"),
            "Attribute VB_Name = \"mFlags\"\r\nPublic Const FT = True\r\nPublic Const FF = False\r\nPublic Const FX = True\r\n");
        File.WriteAllText(Path.Combine(src, "modA.bas"),
            "Sub A()\r\n" +
            "    If FX Then\r\n        If FF Then\r\n            a = 1\r\n        End If\r\n        b = 1\r\n    End If\r\n" +   // dış blok dokunulmaz, iç işlenir
            "    If FT And FX Then\r\n        c = 1\r\n    End If\r\n" +                                          // başka bayrakla birlikte: dokunulmaz
            "    If y Then\r\n        d = 1\r\n    ElseIf FX Then\r\n        d = 2\r\n    End If\r\n" +              // dalın birinde: blok aynı
            "    If FX Then e = 1 Else e = 2\r\n" +                                                              // tek satırlık If
            "    If FT Then\r\n        f = 1\r\n    End If\r\n" +                                                   // diğer bayraklar işlenir
            "End Sub\r\n");
        var outDir = TestPaths.NewTempDir();
        var report = ProjectProcessor.Run(new ProcessOptions
        {
            FlagsPath = Path.Combine(src, "mFlags.bas"), SourceDir = src, OutputDir = outDir, SkipFlags = ["fx"], // harf duyarsız
        });
        Assert.Equal(
            "Sub A()\r\n" +
            "    If FX Then\r\n        b = 1\r\n    End If\r\n" +
            "    If FT And FX Then\r\n        c = 1\r\n    End If\r\n" +
            "    If y Then\r\n        d = 1\r\n    ElseIf FX Then\r\n        d = 2\r\n    End If\r\n" +
            "    If FX Then e = 1 Else e = 2\r\n" +
            "    f = 1\r\n" +
            "End Sub\r\n", File.ReadAllText(Path.Combine(outDir, "modA.bas")));
        Assert.Contains("Dokunulmayan bayraklar: fx", report.ToText());
    }

    [Fact]
    public void Skip_defines_leave_their_directive_groups_untouched()
    {
        var src = TestPaths.NewTempDir();
        File.WriteAllText(Path.Combine(src, "mFlags.bas"), "Attribute VB_Name = \"mFlags\"\r\nPublic Const FF = False\r\n");
        File.WriteAllText(Path.Combine(src, "P.vbp"), "Type=Exe\r\nCondComp=\"DEM = 1 : DBG = 0\"\r\n");
        File.WriteAllText(Path.Combine(src, "modA.bas"),
            "Sub A()\r\n" +
            "#If DEM = 1 Then\r\n    If FF Then\r\n        a = 1\r\n    End If\r\n    b = 1\r\n#Else\r\n    b = 2\r\n#End If\r\n" +
            "#If DBG Then\r\n    c = 1\r\n#End If\r\n" +
            "End Sub\r\n");
        var outDir = TestPaths.NewTempDir();
        var report = ProjectProcessor.Run(new ProcessOptions
        {
            FlagsPath = Path.Combine(src, "mFlags.bas"), SourceDir = src, OutputDir = outDir, SkipConstants = ["DEM"],
        });
        Assert.Equal(
            "Sub A()\r\n" +
            "#If DEM = 1 Then\r\n    b = 1\r\n#Else\r\n    b = 2\r\n#End If\r\n" +   // DEM grubu kaldı, içindeki bayrak If'i işlendi
            "End Sub\r\n", File.ReadAllText(Path.Combine(outDir, "modA.bas")));  // DBG grubu yine çözüldü
        Assert.DoesNotContain(report.Files.Single().Warnings, w => w.Message.Contains("DEM"));
        Assert.Contains("Dokunulmayan #If sabitleri: DEM", report.ToText());
    }

    [Fact]
    public void Constant_table_lists_defined_used_undefined_local_and_skipped_constants()
    {
        var src = TestPaths.NewTempDir();
        File.WriteAllText(Path.Combine(src, "mFlags.bas"), "Attribute VB_Name = \"mFlags\"\r\nPublic Const FT = True\r\n");
        File.WriteAllText(Path.Combine(src, "P.vbp"), "Type=Exe\r\nCondComp=\"DBG = 1 : KULLANILMAYAN = 0\"\r\n");
        File.WriteAllText(Path.Combine(src, "modA.bas"),
            "#Const YEREL = 1\r\nSub A()\r\n" +
            "#If DBG And DEM = 1 Then\r\n    a = 1\r\n#End If\r\n" +
            "#If YEREL Then\r\n    b = 1\r\n#End If\r\n" +
            "#If Win32 Then\r\n    c = 1\r\n#End If\r\n" +
            "End Sub\r\n");
        var report = ProjectProcessor.Run(new ProcessOptions
        {
            FlagsPath = Path.Combine(src, "mFlags.bas"), SourceDir = src, DryRun = true, SkipConstants = ["THE"],
        });
        ConstantRow Row(string n) => report.ConstantTable.Single(r => r.Name == n);
        Assert.Equal(("1", "kullanılıyor", 1), (Row("DBG").Value, Row("DBG").Status, Row("DBG").Uses));
        Assert.Contains("P.vbp", Row("DBG").Source);
        Assert.Equal("tanımlı, kullanılmıyor", Row("KULLANILMAYAN").Status);
        Assert.Equal(("0", "tanımsız: 0 sayıldı"), (Row("DEM").Value, Row("DEM").Status));
        Assert.Equal(["modA.bas"], Row("DEM").Files);
        Assert.Equal("1", Row("YEREL").Value);
        Assert.Contains("#Const", Row("YEREL").Source);
        Assert.Equal("VB yerleşik", Row("Win32").Source);
        Assert.StartsWith("dokunulmuyor", Row("THE").Status);
        Assert.Contains("Sabit tablosu", report.ToText());
    }

    [Fact]
    public void Output_inside_source_is_refused()
        => Assert.Throws<ArgumentException>(() => ProjectProcessor.Run(Options(Path.Combine(TestPaths.SamplesDir, "out"))));

    /// <summary>Junction (mklink /J) yönetici izni gerektirmez.</summary>
    private static void Junction(string link, string target)
    {
        var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;
        p.WaitForExit();
        Assert.True(Directory.Exists(link), "junction oluşturulamadı");
    }

    private static string CopySamples()
    {
        var src = Path.Combine(TestPaths.NewTempDir(), "src");
        Directory.CreateDirectory(Path.Combine(src, "alt"));
        foreach (var f in Directory.GetFiles(TestPaths.SamplesDir)) File.Copy(f, Path.Combine(src, Path.GetFileName(f)));
        File.Copy(Path.Combine(src, "modGenel.bas"), Path.Combine(src, "alt", "modGenel.bas"));
        return src;
    }

    [Fact]
    public void Junction_to_source_as_output_is_refused()
    {
        var src = CopySamples();
        var link = Path.Combine(Path.GetDirectoryName(src)!, "outlink");
        Junction(link, src);
        var before = Directory.GetFiles(src, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllBytes);

        var o = new ProcessOptions { FlagsPath = Path.Combine(src, "mDevelopmentFlags.bas"), SourceDir = src, OutputDir = link };
        var ex = Assert.Throws<ArgumentException>(() => ProjectProcessor.Run(o));
        Assert.Contains("bağlantı çözüldü", ex.Message);

        // çıktının içindeki bir junction da kaynağa yazdıramaz
        var outDir = Path.Combine(Path.GetDirectoryName(src)!, "out");
        Directory.CreateDirectory(outDir);
        Junction(Path.Combine(outDir, "alt"), Path.Combine(src, "alt"));
        o = new ProcessOptions { FlagsPath = Path.Combine(src, "mDevelopmentFlags.bas"), SourceDir = src, OutputDir = outDir };
        Assert.Throws<InvalidOperationException>(() => ProjectProcessor.Run(o));

        foreach (var (f, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(f));
    }

    [Fact]
    public void Report_path_cannot_be_a_source_or_flags_file()
    {
        var src = CopySamples();
        var flags = Path.Combine(src, "mDevelopmentFlags.bas");
        var guard = new WriteGuard(src, flags);
        Assert.Throws<InvalidOperationException>(() => guard.CheckFile(Path.Combine(src, "frmMain.frm")));
        Assert.Throws<InvalidOperationException>(() => guard.CheckFile(Path.Combine(src, "yeni-rapor.txt")));
        guard.CheckFile(Path.Combine(Path.GetDirectoryName(src)!, "rapor.txt")); // kaynak dışı: sorun yok

        var outsideFlags = Path.Combine(TestPaths.NewTempDir(), "flags.bas");
        File.WriteAllText(outsideFlags, "Public Const T1 = True\r\n");
        Assert.Throws<InvalidOperationException>(() => new WriteGuard(src, outsideFlags).CheckFile(outsideFlags));
    }

    [Fact]
    public void Local_declaration_shadowing_flag_makes_it_unknown()
    {
        var src = TestPaths.NewTempDir();
        File.WriteAllText(Path.Combine(src, "mFlags.bas"), "Attribute VB_Name = \"mFlags\"\r\nPublic Const T1 = True\r\n");
        File.WriteAllText(Path.Combine(src, "frmA.frm"), "Sub X()\r\n    Dim T1 As Boolean\r\n    If T1 Then\r\n        a = 1\r\n    End If\r\nEnd Sub\r\n");
        var report = ProjectProcessor.Run(new ProcessOptions { FlagsPath = Path.Combine(src, "mFlags.bas"), SourceDir = src, DryRun = true });
        Assert.False(report.Flags.TryGetKnown("T1", out _));
        Assert.False(report.Files.Single().Changed);
    }
}
