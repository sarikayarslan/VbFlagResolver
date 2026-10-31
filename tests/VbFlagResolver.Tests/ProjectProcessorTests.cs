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

        Assert.Contains("Okunan bayrak : 23", report.ToText());
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
