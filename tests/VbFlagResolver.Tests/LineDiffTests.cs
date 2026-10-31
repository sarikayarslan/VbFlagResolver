using VbFlagResolver.Core;

namespace VbFlagResolver.Tests;

public class LineDiffTests
{
    private static string Render(List<DiffLine> d)
        => string.Join("|", d.Select(l => (l.Kind switch { DiffKind.Same => " ", DiffKind.Removed => "-", _ => "+" }) + l.Text));

    [Fact]
    public void Identical_inputs_have_no_changes()
    {
        string[] a = ["a", "b", "c"];
        var d = LineDiff.Compute(a, a);
        Assert.All(d, l => Assert.Equal(DiffKind.Same, l.Kind));
        Assert.Empty(LineDiff.Hunks(d));
    }

    [Fact]
    public void Finds_minimal_edits()
    {
        var d = LineDiff.Compute(["a", "If X", "b", "End If", "c"], ["a", "b", "c"]);
        Assert.Equal(" a|-If X| b|-End If| c", Render(d));
        Assert.Equal([1, 2, 3, 4, 5], d.Where(l => l.OldNo > 0).Select(l => l.OldNo));
        Assert.Equal([1, 2, 3], d.Where(l => l.NewNo > 0).Select(l => l.NewNo));
    }

    [Fact]
    public void Replacements_and_insertions()
    {
        var d = LineDiff.Compute(["x", "  a", "y"], ["x", "a", "y", "z"]);
        Assert.Equal(" x|-  a|+a| y|+z", Render(d));
    }

    [Fact]
    public void Hunks_merge_nearby_changes_and_keep_context()
    {
        var a = Enumerable.Range(1, 30).Select(i => i.ToString()).ToArray();
        var b = a.Where(s => s != "5" && s != "8" && s != "25").ToArray();
        var hunks = LineDiff.Hunks(LineDiff.Compute(a, b), context: 2);
        Assert.Equal(2, hunks.Count);
        Assert.Equal("3", hunks[0][0].Text);
        Assert.Equal("10", hunks[0][^1].Text);
    }

    [Fact]
    public void Split_lines_handles_crlf_and_final_newline()
    {
        Assert.Equal(["a", "b"], LineDiff.SplitLines("a\r\nb\r\n"));
        Assert.Equal(["a", "b"], LineDiff.SplitLines("a\nb"));
    }
}
