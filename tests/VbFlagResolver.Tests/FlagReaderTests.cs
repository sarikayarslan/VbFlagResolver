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
        Assert.Equal(23, r.Flags.Count);
        Assert.Equal("mDevelopmentFlags", r.ModuleName);
        Assert.Contains(r.Unrecognized, u => u.Text!.Contains("APP_VERSION"));
        Assert.Contains(r.Unrecognized, u => u.Text!.Contains("MAX_KAYIT"));
        Assert.Contains(r.Unrecognized, u => u.Text!.Contains("Declare Function"));
        Assert.Contains(r.Unrecognized, u => u.Text!.Contains("gsKullanici"));
        Assert.False(r.Flags.IsFlag("APP_VERSION"));
        Assert.Contains(r.Notes, n => n.Message.Contains("gbFlagHicAtanmamis"));
    }

    [Theory]
    [InlineData("FLAG_YENI_RAPOR", "Public Const As Boolean", "doğrudan değer: True")]
    [InlineData("FLAG_LOG_DETAY", "Public Const (tipsiz)", "doğrudan değer: True")]
    [InlineData("FLAG_KDV_YENI", "Global Const (tipsiz)", "doğrudan değer: True")]
    [InlineData("FLAG_YEREL_TEST", "Const (tipsiz)", "doğrudan değer: False")]
    [InlineData("FLAG_B", "Public Const (tipsiz)", "aynı satırda birden çok tanım")]
    [InlineData("FLAG_UZUN_ISIMLI_GECICI_BAYRAK", "Public Const As Boolean", "satır devamıyla")]
    [InlineData("FLAG_TURETILMIS", "Public Const (tipsiz)", "ifadeden hesaplandı: FLAG_A And Not FLAG_B")]
    [InlineData("gbFlagMusteriV2", "Public ... As Boolean (değişken)", "InitFlags içinde satır")]
    [InlineData("gbFlagDebugPanel", "Public ... As Boolean (değişken)", "aynı satırda birden çok tanım; InitFlags içinde satır")]
    [InlineData("gbFlagHicAtanmamis", "Public ... As Boolean (değişken)", "hiç atanmamış → VB varsayılanı False")]
    public void Describes_how_each_flag_was_read(string name, string style, string howContains)
    {
        var f = ReadSample().Flags.Get(name)!;
        Assert.Equal(style, f.Style);
        Assert.Contains(howContains, f.HowRead, StringComparison.OrdinalIgnoreCase); // VB adları harf duyarsız (InitFlags = InitFLags)
        Assert.Contains(name, f.Declaration, StringComparison.OrdinalIgnoreCase);
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

    private static FlagReadResult ReadText(string src, FlagReaderOptions? o = null)
        => FlagReader.Read(VbSourceFile.FromText("m.bas", src), o);

    private static bool? Value(FlagReadResult r, string name)
        => r.Flags.TryGetKnown(name, out var v) ? v : null;

    [Fact]
    public void Same_procedure_last_assignment_wins()
    {
        // şirketteki düzen: üstte bildirimler, yordamda True'lar, en altta bazıları False
        var r = ReadText("""
            Public gbA As Boolean
            Public gbB As Boolean
            Public gbC As Boolean
            Public Sub BayraklariAyarla()
                gbA = True
                gbB = True
                gbC = True

                ' en altta kapatılanlar
                gbA = False
                gbC = False
            End Sub
            """);
        Assert.False(Value(r, "gbA"));
        Assert.True(Value(r, "gbB"));
        Assert.False(Value(r, "gbC"));
        var a = r.Flags.Get("gbA")!;
        Assert.Equal(10, a.AssignmentLine);
        Assert.Contains("öncekiler: BayraklariAyarla satır 5: gbA = True → son atama geçerli", a.HowRead);
        Assert.Contains(r.Notes, n => n.Message.Contains("gbA") && n.Message.Contains("son atama (False) geçerli"));
    }

    [Fact]
    public void Different_procedures_with_different_values_are_unknown()
    {
        var src = """
            Public gbA As Boolean, gbB As Boolean
            Sub InitFlags()
                gbA = True
                gbB = True
            End Sub
            Sub KapatFlags()
                gbA = False
                gbB = True
            End Sub
            """;
        var r = ReadText(src);
        Assert.Null(Value(r, "gbA"));
        Assert.Contains("hangisinin son çalıştığı bilinmiyor", r.Flags.Get("gbA")!.UnknownReason);
        Assert.True(Value(r, "gbB")); // aynı değer: sorun yok

        // yalnızca InitFlags ilk değer yordamı sayılırsa diğer yordamdaki atama yine Bilinmiyor yapar
        var r2 = ReadText(src, new FlagReaderOptions { InitProcedures = ["InitFlags"] });
        Assert.Null(Value(r2, "gbA"));
        Assert.Null(Value(r2, "gbB"));
    }

    // metinde InitFlags üstte (True), XFonksiyonu en altta (False)
    private const string TwoProcs = """
        Public gbA As Boolean, gbB As Boolean
        Public Sub InitFlags()
            gbA = True
            gbB = True
        End Sub

        Public Sub XFonksiyonu()
            gbA = False
        End Sub
        """;

    [Fact]
    public void Unknown_call_order_suggests_specifying_it()
    {
        var r = ReadText(TwoProcs);
        Assert.Null(Value(r, "gbA"));
        Assert.Contains("Çağrı sırasını", r.Flags.Get("gbA")!.UnknownReason);
        Assert.True(Value(r, "gbB"));
        Assert.Contains(r.Notes, n => n.Message.Contains("(InitFlags, XFonksiyonu)"));
    }

    [Theory]
    [InlineData("XFonksiyonu,InitFlags", true)]   // önce False, sonra True → True
    [InlineData("InitFlags,XFonksiyonu", false)]  // önce True, sonra False → False
    public void Given_call_order_decides_last_assignment(string order, bool expected)
    {
        var r = ReadText(TwoProcs, new FlagReaderOptions { InitProcedures = order.Split(',') });
        Assert.Equal(expected, Value(r, "gbA"));
        Assert.True(Value(r, "gbB"));
        Assert.Contains("son atama geçerli", r.Flags.Get("gbA")!.HowRead);
    }

    [Theory]
    [InlineData("    XFonksiyonu\n    gbA = True", true)]       // önce çağrı (False), sonra True
    [InlineData("    gbA = True\n    Call XFonksiyonu", false)] // önce True, sonra çağrı (False)
    public void Calls_inside_flags_module_are_followed(string initBody, bool expected)
    {
        var r = ReadText($"""
            Public gbA As Boolean
            Public Sub InitFlags()
            {initBody}
            End Sub

            Public Sub XFonksiyonu()
                gbA = False
            End Sub
            """);
        Assert.Equal(expected, Value(r, "gbA"));
        var procs = r.Procedures.ToDictionary(p => p.Name);
        Assert.True(procs["InitFlags"].IsRoot);
        Assert.False(procs["XFonksiyonu"].IsRoot);
        Assert.Equal(["XFonksiyonu"], procs["InitFlags"].Calls);
    }

    [Fact]
    public void Conditionally_called_procedure_makes_its_assignments_unknown()
    {
        var r = ReadText("""
            Public gbA As Boolean, gbB As Boolean
            Public Sub InitFlags()
                gbA = True
                gbB = True
                If Command$ = "/eski" Then XFonksiyonu
                gbB = True
            End Sub
            Public Sub XFonksiyonu()
                gbA = False
                gbB = False
            End Sub
            """);
        Assert.Null(Value(r, "gbA"));   // XFonksiyonu çağrılırsa False, çağrılmazsa True
        Assert.True(Value(r, "gbB"));   // sonradan koşulsuz True
    }

    [Fact]
    public void Missing_init_procedure_is_reported()
    {
        var r = ReadText(TwoProcs, new FlagReaderOptions { InitProcedures = ["InitFlags", "Yok"] });
        Assert.Contains(r.Notes, n => n.Message.Contains("bulunamadı: Yok"));
        Assert.Null(Value(r, "gbA")); // XFonksiyonu sıraya yazılmadı ve çağrılmıyor
    }

    [Fact]
    public void Conditional_assignment_is_unknown_unless_followed_by_unconditional_one()
    {
        var r = ReadText("""
            Public gbA As Boolean, gbB As Boolean
            Sub InitFlags()
                If Command$ = "" Then
                    gbA = True
                    gbB = True
                End If
                gbB = False
            End Sub
            """);
        Assert.Null(Value(r, "gbA"));
        Assert.False(Value(r, "gbB"));
    }

    [Fact]
    public void Assignments_after_exit_are_ignored_or_conditional()
    {
        var r = ReadText("""
            Public gbA As Boolean, gbB As Boolean, gbC As Boolean
            Sub InitFlags()
                gbA = True
                gbC = True
                If Command$ = "" Then Exit Sub
                gbC = False
                Exit Sub
                gbA = False
                gbB = True
            End Sub
            """);
        Assert.True(Value(r, "gbA"));   // Exit Sub'dan sonraki atama çalışmaz
        Assert.False(Value(r, "gbB"));  // yalnızca erişilemez atama var → atanmamış → False
        Assert.Null(Value(r, "gbC"));   // koşullu Exit'ten sonra: çalışıp çalışmadığı bilinmiyor
        Assert.Contains(r.Unrecognized, u => u.Message.Contains("Erişilemez"));
    }

    [Fact]
    public void Repeated_same_value_is_fine()
    {
        var r = ReadText("""
            Public gbC As Boolean
            Sub InitFlags()
                gbC = True
                gbC = True
            End Sub
            """);
        Assert.True(Value(r, "gbC"));
    }

    private const string DirectiveFlags = """
        #If DEBUG_MODE Then
        Public Const FLAG_D = True
        #Else
        Public Const FLAG_D = False
        #End If
        Public gbX As Boolean
        Sub InitFlags()
        #If DEBUG_MODE Then
            gbX = True
        #Else
            gbX = False
        #End If
        End Sub
        """;

    private static CondConstants Defines(string spec)
    {
        var c = new CondConstants();
        foreach (var (n, v) in CondConstants.ParseList(spec)) c.Set(n, v, "test");
        return c;
    }

    [Theory]
    [InlineData("DEBUG_MODE=1", true)]
    [InlineData("DEBUG_MODE=0", false)]
    public void Flags_inside_directives_follow_compiled_branch(string defines, bool expected)
    {
        var r = ReadText(DirectiveFlags, new FlagReaderOptions { Constants = Defines(defines) });
        Assert.Equal(expected, Value(r, "FLAG_D"));
        Assert.Equal(expected, Value(r, "gbX"));
        Assert.DoesNotContain(r.Notes, n => n.Message.Contains("birden çok kez"));
    }

    [Fact]
    public void Flags_inside_unresolved_directives_are_unknown()
    {
        var r = ReadText(DirectiveFlags); // sabit yok → DEBUG_MODE tanımsız
        Assert.Null(Value(r, "FLAG_D"));
        Assert.Null(Value(r, "gbX"));
        Assert.Contains("#If DEBUG_MODE içinde tanımlı", r.Flags.Get("FLAG_D")!.UnknownReason);
        Assert.Contains("#If DEBUG_MODE içinde atanıyor", r.Flags.Get("gbX")!.UnknownReason);

        // çözme kapalıyken de aynı güvenli sonuç
        var off = ReadText(DirectiveFlags, new FlagReaderOptions { Constants = Defines("DEBUG_MODE=1"), ResolveDirectives = false });
        Assert.Null(Value(off, "FLAG_D"));
        Assert.Null(Value(off, "gbX"));
    }

    [Fact]
    public void Assignment_outside_procedure_makes_flag_unknown()
    {
        var r = ReadText("""
            Public gbA As Boolean
            Public Sub InitFlags()
                gbA = True
            End Sub

            gbA = False
            """);
        Assert.Null(Value(r, "gbA"));
        Assert.Contains("yordam dışında", r.Flags.Get("gbA")!.UnknownReason);
        Assert.Contains(r.Notes, n => n.Line == 6 && n.Message.Contains("derlemez"));
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
