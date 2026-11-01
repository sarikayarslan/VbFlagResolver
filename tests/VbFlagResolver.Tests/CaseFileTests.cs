using VbFlagResolver.Core;

namespace VbFlagResolver.Tests;

/// <summary>Cases/NN_ad.input.bas → Cases/NN_ad.expected.bas dosya çiftleri (her dönüşüm kuralı için bir çift).</summary>
public class CaseFileTests
{
    public static TheoryData<string> Cases()
    {
        var data = new TheoryData<string>();
        foreach (var f in Directory.GetFiles(TestPaths.CasesDir, "*.input.bas").OrderBy(f => f))
            data.Add(Path.GetFileName(f)[..^".input.bas".Length]);
        return data;
    }

    /// <summary>#If testleri için derleme sabitleri (Cases/_defines.txt: DEBUG_MODE = 1, YENI = 0).</summary>
    public static CondConstants CaseConstants()
    {
        var c = new CondConstants();
        foreach (var (n, v) in CondConstants.ParseList(File.ReadAllText(Path.Combine(TestPaths.CasesDir, "_defines.txt"))))
            c.Set(n, v, "_defines.txt");
        return c;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Input_produces_expected_output(string name)
    {
        var flags = TestPaths.CaseFlags().Flags;
        var input = VbSourceFile.Load(Path.Combine(TestPaths.CasesDir, name + ".input.bas"));
        var expected = File.ReadAllBytes(Path.Combine(TestPaths.CasesDir, name + ".expected.bas"));

        var result = new Rewriter(flags, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "mFlags" }, CaseConstants()).Rewrite(input);

        var enc = VbSourceFile.GetEncoding(1254);
        Assert.Equal(enc.GetString(expected), enc.GetString(result.Output));
        Assert.Equal(expected, result.Output);
    }

    [Fact]
    public void Block_crossing_unresolved_directive_is_left_with_warning()
    {
        var input = VbSourceFile.Load(Path.Combine(TestPaths.CasesDir, "27_directive_crossing_unchanged.input.bas"));
        var result = new Rewriter(TestPaths.CaseFlags().Flags, null, CaseConstants()).Rewrite(input);
        Assert.Contains(result.Report.Warnings, w => w.Message.Contains("kesişiyor"));
        Assert.Contains(result.Report.Warnings, w => w.Message.Contains("TANIMSIZ"));
    }

    [Fact]
    public void Directives_are_left_alone_when_resolution_is_off()
    {
        var input = VbSourceFile.Load(Path.Combine(TestPaths.CasesDir, "24_directive_resolve.input.bas"));
        var result = new Rewriter(TestPaths.CaseFlags().Flags, null, CaseConstants(), resolveDirectives: false).Rewrite(input);
        Assert.False(result.Changed);
    }

    [Fact]
    public void Removed_const_still_used_produces_warning()
    {
        var input = VbSourceFile.Load(Path.Combine(TestPaths.CasesDir, "31_needed_decl_block_kept.input.bas"));
        var result = new Rewriter(TestPaths.CaseFlags().Flags).Rewrite(input);
        Assert.Contains(result.Report.Warnings, w => w.Message.Contains("Const bildirimi 'M'"));
    }

    [Fact]
    public void Date_literal_detection_does_not_swallow_file_numbers()
    {
        // "#1: If F1 Then Close #" tarih sanılırsa tek satırlık If görünmez
        Assert.Equal("Sub X()\r\n    Close #1\r\nEnd Sub\r\n",
            TestPaths.RewriteText("Sub X()\r\n    Close #1: If F1 Then Close #2\r\nEnd Sub\r\n"));
        Assert.Contains(Lexer.Tokenize("d = #1/31/2000 2:30 PM#"), t => t.Kind == TokenKind.Date);
        Assert.Contains(Lexer.Tokenize("d = #14:30#"), t => t.Kind == TokenKind.Date);
    }

    [Fact]
    public void Unexpected_error_in_one_file_does_not_abort()
    {
        // Rewriter dışarı istisna sızdırmaz; dosya değiştirilmeden ve hatalı olarak döner
        var file = VbSourceFile.FromText("x.bas", "Sub X()\r\n#If &HFFFFFFFFFFFFFFFFF Then\r\n    a = 1\r\n#End If\r\nEnd Sub\r\n");
        var r = new Rewriter(TestPaths.CaseFlags().Flags).Rewrite(file);
        Assert.False(r.Report.Failed); // taşan sayı artık bilinmiyor sayılıyor; grup olduğu gibi kalır
        Assert.Equal(file.OriginalBytes, r.Output);
    }

    [Fact]
    public void Unclean_header_produces_warning()
    {
        var input = VbSourceFile.Load(Path.Combine(TestPaths.CasesDir, "21_unclean_header_unchanged.input.bas"));
        var result = new Rewriter(TestPaths.CaseFlags().Flags).Rewrite(input);
        Assert.False(result.Changed);
        Assert.Contains(result.Report.Warnings, w => w.Message.Contains("değiştirilmedi"));
    }
}
