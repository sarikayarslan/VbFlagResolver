using VbFlagResolver.Core;

namespace VbFlagResolver.Tests;

public class DirectiveTests
{
    private static CondConstants Consts()
    {
        var c = new CondConstants();
        foreach (var (n, v) in CondConstants.ParseList("A = 1 : B = 0 : S = \"x\""))
            c.Set(n, v, "test");
        c.Set("U", null, "test"); // tanımlı ama bilinmiyor
        return c;
    }

    private static bool? Eval(string expr, out List<string> undefined)
        => DirectiveEvaluator.Evaluate(Lexer.Tokenize(expr), Consts(), out undefined).Truth();

    [Theory]
    [InlineData("A", true)]
    [InlineData("B", false)]
    [InlineData("a", true)] // harf duyarsız
    [InlineData("Not B", true)]
    [InlineData("Not A", true)] // Not 1 = -2 (bit düzeyinde) → sıfırdan farklı, VB'deki gibi
    [InlineData("A And B", false)]
    [InlineData("A = 1", true)]
    [InlineData("A <> 1", false)]
    [InlineData("A > 0 And B = 0", true)]
    [InlineData("(A + 1) * 2 = 4", true)]
    [InlineData("&H10 = 16", true)]
    [InlineData("S = \"x\"", true)]
    [InlineData("True Or C", true)]   // -1 Or x = -1
    [InlineData("B And C", false)]    // 0 And x = 0
    [InlineData("False", false)]
    [InlineData("A Xor 1", false)]
    [InlineData("Win32", true)]                // VB5/VB6 yerleşik sabitleri
    [InlineData("Win16", false)]
    [InlineData("&HFFFF = -1", true)]          // VB: işaretli Integer
    [InlineData("&HFFFF& = 65535", true)]      // & eki: Long
    [InlineData("&H8000 < 0", true)]
    [InlineData("&HFFFFFFFF = -1", true)]      // Long
    [InlineData("&O177777 = -1", true)]
    public void Evaluates_conditions(string expr, bool expected)
        => Assert.Equal(expected, Eval(expr, out _));

    [Theory]
    [InlineData("C", "C")]
    [InlineData("A And C", "C")]
    [InlineData("A Or C", "C")] // 1 Or x bilinmez (yalnızca -1 kesinleştirir)
    [InlineData("U", null)]     // tanımlı ama değeri bilinmiyor
    public void Unknown_or_undefined_constants_give_unknown(string expr, string? undefinedName)
    {
        Assert.Null(Eval(expr, out var undefined));
        if (undefinedName != null) Assert.Contains(undefinedName, undefined);
    }

    [Theory]
    [InlineData("&HFFFFFFFFFFFFFFFFF")]   // 64 bitten büyük: çökme yerine bilinmiyor
    [InlineData("&H100000000 = 0")]       // 32 bitten büyük: VB'de derlenmez
    public void Too_large_literals_give_unknown(string expr)
        => Assert.Null(Eval(expr, out _));

    [Fact]
    public void Invalid_user_defines_are_reported()
    {
        Assert.Equal(["X", "=1"], CondConstants.InvalidEntries("A=1, X, =1, B = True"));
        var notes = new List<SourceNote>();
        var c = CondConstants.FromProject(null, "A=1, X", notes);
        Assert.True(c.TryGet("A", out _));
        Assert.Contains(notes, n => n.Message.Contains("'X'"));
    }

    [Fact]
    public void Parses_vbp_and_user_lists()
    {
        var vbp = CondConstants.ParseList("DEBUG_MODE = 1 : YENI = 0");
        Assert.Equal(["DEBUG_MODE", "YENI"], vbp.Select(x => x.Name));
        Assert.Equal(1, vbp[0].Value!.Value.Number);
        var user = CondConstants.ParseList("DEBUG_MODE=0, X = 2");
        Assert.Equal(0, user[0].Value!.Value.Number);
        Assert.Equal(2, user[1].Value!.Value.Number);
    }

    [Fact]
    public void Reads_condcomp_from_vbp_and_user_overrides_it()
    {
        var dir = TestPaths.NewTempDir();
        File.WriteAllText(Path.Combine(dir, "Proje.vbp"), "Type=Exe\r\nCondComp=\"DEBUG_MODE = 1 : YENI = 0\"\r\n");
        var notes = new List<SourceNote>();

        var c = CondConstants.FromProject(dir, null, notes);
        Assert.True(c.TryGet("DEBUG_MODE", out var d) && d!.Value.Number == 1);
        Assert.Contains("Proje.vbp", c.All.First().Source);

        var o = CondConstants.FromProject(dir, "DEBUG_MODE=0", notes);
        Assert.True(o.TryGet("DEBUG_MODE", out var d2) && d2!.Value.Number == 0);
        Assert.True(o.TryGet("YENI", out var y) && y!.Value.Number == 0);
        Assert.Empty(notes);
    }

    [Fact]
    public void Conflicting_vbp_values_make_constant_unknown()
    {
        var dir = TestPaths.NewTempDir();
        File.WriteAllText(Path.Combine(dir, "A.vbp"), "CondComp=\"DEBUG_MODE = 1\"\r\n");
        File.WriteAllText(Path.Combine(dir, "B.vbp"), "CondComp=\"DEBUG_MODE = 0\"\r\n");
        var notes = new List<SourceNote>();
        var c = CondConstants.FromProject(dir, null, notes);
        Assert.True(c.TryGet("DEBUG_MODE", out var v));
        Assert.Null(v);
        Assert.Single(notes);
    }

    [Fact]
    public void Constant_defined_in_only_one_of_several_vbp_files_is_unknown()
    {
        // ana proje DEM'i tanımlamıyor (VB'de 0), demo projesi DEM = 1: hangisi temizleniyor bilinemez
        var dir = TestPaths.NewTempDir();
        File.WriteAllText(Path.Combine(dir, "Proje.vbp"), "Type=Exe\r\nStartup=\"Sub Main\"\r\n");
        File.WriteAllText(Path.Combine(dir, "ProjeDemo.vbp"), "Type=Exe\r\nCondComp=\"DEM = 1 : ORTAK = 1\"\r\n");
        File.WriteAllText(Path.Combine(dir, "Proje2.vbp"), "Type=Exe\r\nCondComp=\"ORTAK = 1\"\r\n");
        var notes = new List<SourceNote>();
        var c = CondConstants.FromProject(dir, null, notes);
        Assert.True(c.TryGet("DEM", out var dem));
        Assert.Null(dem);
        Assert.True(c.TryGet("ORTAK", out var ortak)); // Proje.vbp'de tanımsız (0), diğerlerinde 1
        Assert.Null(ortak);
        Assert.Contains(notes, n => n.Message.Contains("DEM") && n.Message.Contains("Proje.vbp: tanımsız (0)"));

        // kullanıcı değeri verince çözülür
        var o = CondConstants.FromProject(dir, "DEM=0, ORTAK=1", new List<SourceNote>());
        Assert.True(o.TryGet("DEM", out var d0) && d0!.Value.Number == 0);
    }

    [Fact]
    public void Single_top_level_vbp_wins_over_vbp_files_in_subfolders()
    {
        var dir = TestPaths.NewTempDir();
        Directory.CreateDirectory(Path.Combine(dir, "Yedek"));
        File.WriteAllText(Path.Combine(dir, "Proje.vbp"), "Type=Exe\r\nCondComp=\"DEBUG_MODE = 0\"\r\n");
        File.WriteAllText(Path.Combine(dir, "Yedek", "Eski.vbp"), "Type=Exe\r\nCondComp=\"DEM = 1 : DEBUG_MODE = 1\"\r\n");
        var notes = new List<SourceNote>();
        var c = CondConstants.FromProject(dir, null, notes);
        Assert.False(c.TryGet("DEM", out _));                                  // yedekteki tanım alınmadı
        Assert.True(c.TryGet("DEBUG_MODE", out var d) && d!.Value.Number == 0);
        Assert.Contains(notes, n => n.Message.Contains("yok sayıldı") && n.Message.Contains("Eski.vbp"));
    }

    [Fact]
    public void Module_const_overrides_project_value_inside_that_file()
    {
        var file = VbSourceFile.FromText("m.bas", "#Const DEBUG_MODE = 0\r\n#If DEBUG_MODE Then\r\na = 1\r\n#Else\r\na = 2\r\n#End If\r\n");
        var project = new CondConstants();
        project.Set("DEBUG_MODE", DValue.Num(1), "vbp");
        var r = DirectiveResolver.Resolve(file.GetLogicalLines(), project, resolve: true, "m.bas");
        Assert.Equal(["#Const DEBUG_MODE = 0", "a = 2"], r.Kept.Select(l => l.Text));
        Assert.True(project.TryGet("DEBUG_MODE", out var still) && still!.Value.Number == 1); // proje sabiti değişmedi
    }

    [Fact]
    public void Unbalanced_directives_are_reported_and_left()
    {
        var file = VbSourceFile.FromText("m.bas", "#If A Then\r\na = 1\r\n");
        var r = DirectiveResolver.Resolve(file.GetLogicalLines(), Consts(), resolve: true, "m.bas");
        Assert.NotNull(r.Error);
        Assert.Equal(2, r.Kept.Count);
        Assert.False(r.Changed);
    }
}
