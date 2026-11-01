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
