using VbFlagResolver.Core;

namespace VbFlagResolver.Tests;

public class FlagReaderTests
{
    private static FlagReadResult ReadSample(FlagReaderOptions? o = null)
        => FlagReader.Read(Path.Combine(TestPaths.SamplesDir, "mDevelopmentFlags.bas"), o);

    [Theory]
    [InlineData("FLAG_YENI_RAPOR", true)]          // Public Const X As Boolean = True
    [InlineData("FLAG_ESKI_MENU", false)]
    [InlineData("FLAG_LOG_DETAY", true)]           // Public Const X = True
    [InlineData("FLAG_BETA_EKRAN", false)]         // yorumlu
    [InlineData("FLAG_KDV_YENI", true)]            // Global Const
    [InlineData("FLAG_ESKI_YAZICI", false)]
    [InlineData("FLAG_YEREL_TEST", false)]         // Const (kapsam yok)
    [InlineData("FLAG_A", true)]                   // aynı satırda birden çok
    [InlineData("FLAG_B", false)]
    [InlineData("FLAG_C", true)]
    [InlineData("FLAG_UZUN_ISIMLI_GECICI_BAYRAK", true)] // satır devamı
    [InlineData("FLAG_X", true)]
    [InlineData("FLAG_Y", false)]                  // virgül + satır devamı
    [InlineData("FLAG_TURETILMIS", true)]          // ifade
    [InlineData("FLAG_KAPALI_TURETILMIS", false)]
    [InlineData("FLAG_GIZLI", false)]              // Private Const
    [InlineData("FLAG_KUCUK_HARF", true)]          // büyük/küçük harf duyarsız
    [InlineData("gbFlagMusteriV2", true)]          // değişken + InitFlags
    [InlineData("gbFlagStokSayim", false)]         // ':' ile iki atama
    [InlineData("gbflagdebugpanel", true)]
    [InlineData("gbFlagHicAtanmamis", false)]      // atanmamış → VB varsayılanı False
    [InlineData("gbFlagCalismaZamani", false)]     // dosya tek başına okunduğunda False
    public void Reads_all_definition_styles(string name, bool expected)
    {
        var r = ReadSample();
        Assert.True(r.Flags.TryGetKnown(name, out var v), $"{name} okunamadı");
        Assert.Equal(expected, v);
    }

    [Fact]
    public void Reports_unrecognized_lines_and_skips_them()
    {
        var r = ReadSample();
        Assert.Equal(22, r.Flags.Count);
        Assert.Equal("mDevelopmentFlags", r.ModuleName);
        Assert.Contains(r.Unrecognized, u => u.Text!.Contains("APP_VERSION"));
        Assert.Contains(r.Unrecognized, u => u.Text!.Contains("MAX_KAYIT"));
        Assert.Contains(r.Unrecognized, u => u.Text!.Contains("Declare Function"));
        Assert.Contains(r.Unrecognized, u => u.Text!.Contains("gsKullanici"));
        Assert.False(r.Flags.IsFlag("APP_VERSION"));
        Assert.Contains(r.Notes, n => n.Message.Contains("gbFlagHicAtanmamis"));
    }

    [Fact]
    public void Const_only_mode_ignores_variables()
    {
        var r = ReadSample(new FlagReaderOptions { Mode = FlagReadMode.ConstOnly });
        Assert.True(r.Flags.IsFlag("FLAG_A"));
        Assert.False(r.Flags.IsFlag("gbFlagMusteriV2"));
    }

    [Fact]
    public void Variables_only_mode_ignores_consts()
    {
        var r = ReadSample(new FlagReaderOptions { Mode = FlagReadMode.VariablesOnly });
        Assert.False(r.Flags.IsFlag("FLAG_A"));
        Assert.True(r.Flags.TryGetKnown("gbFlagMusteriV2", out var v) && v);
    }

    [Fact]
    public void Assignment_outside_init_procedure_makes_flag_unknown()
    {
        var src = """
            Attribute VB_Name = "mF"
            Public gbA As Boolean
            Public gbB As Boolean
            Public Sub InitFlags()
                gbA = True
            End Sub
            Public Sub Baska()
                gbB = True
            End Sub
            """;
        var file = VbSourceFile.FromText("mF.bas", src);
        var r = FlagReader.Read(file, new FlagReaderOptions { InitProcedures = ["InitFlags"] });
        Assert.True(r.Flags.TryGetKnown("gbA", out var a) && a);
        Assert.False(r.Flags.TryGetKnown("gbB", out _));
    }

    [Fact]
    public void Conditional_or_conflicting_assignments_are_unknown()
    {
        var src = """
            Public gbA As Boolean, gbB As Boolean, gbC As Boolean
            Sub InitFlags()
                If Command$ = "" Then
                    gbA = True
                End If
                gbB = True
                gbB = False
                gbC = True
                gbC = True
            End Sub
            """;
        var r = FlagReader.Read(VbSourceFile.FromText("m.bas", src));
        Assert.False(r.Flags.TryGetKnown("gbA", out _));
        Assert.False(r.Flags.TryGetKnown("gbB", out _));
        Assert.True(r.Flags.TryGetKnown("gbC", out var c) && c);
    }

    [Fact]
    public void Unassigned_variable_can_be_unknown()
    {
        var r = FlagReader.Read(VbSourceFile.FromText("m.bas", "Public gbA As Boolean\r\n"),
            new FlagReaderOptions { UnassignedVariableIsFalse = false });
        Assert.True(r.Flags.IsFlag("gbA"));
        Assert.False(r.Flags.TryGetKnown("gbA", out _));
    }
}
