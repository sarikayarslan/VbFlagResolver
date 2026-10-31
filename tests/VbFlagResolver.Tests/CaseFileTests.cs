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

    [Theory]
    [MemberData(nameof(Cases))]
    public void Input_produces_expected_output(string name)
    {
        var flags = TestPaths.CaseFlags().Flags;
        var input = VbSourceFile.Load(Path.Combine(TestPaths.CasesDir, name + ".input.bas"));
        var expected = File.ReadAllBytes(Path.Combine(TestPaths.CasesDir, name + ".expected.bas"));

        var result = new Rewriter(flags, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "mFlags" }).Rewrite(input);

        var enc = VbSourceFile.GetEncoding(1254);
        Assert.Equal(enc.GetString(expected), enc.GetString(result.Output));
        Assert.Equal(expected, result.Output);
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
