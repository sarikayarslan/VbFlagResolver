using VbFlagResolver.Core;

namespace VbFlagResolver.Tests;

public class SourceFileTests
{
    [Fact]
    public void Frm_header_is_detected_and_untouched()
    {
        var file = VbSourceFile.Load(Path.Combine(TestPaths.SamplesDir, "frmMain.frm"));
        Assert.Equal("frmMain", file.ModuleName);
        var firstCode = file.Lines[file.HeaderLineCount].Text;
        Assert.Equal("Option Explicit", firstCode);
        Assert.StartsWith("Attribute VB_Exposed", file.Lines[file.HeaderLineCount - 1].Text);

        var flags = FlagReader.Read(Path.Combine(TestPaths.SamplesDir, "mDevelopmentFlags.bas")).Flags;
        var result = new Rewriter(flags).Rewrite(file);
        Assert.True(result.Changed);
        int headerBytes = file.Lines.Take(file.HeaderLineCount).Sum(l => l.Bytes.Length + l.Ending.Length);
        Assert.Equal(file.OriginalBytes.Take(headerBytes), result.Output.Take(headerBytes));
    }

    [Fact]
    public void Turkish_1254_bytes_and_crlf_are_preserved()
    {
        var enc = VbSourceFile.GetEncoding(1254);
        string src = "Sub Test()\r\n    ' Açıklama: ğüşıöç İĞÜŞÖÇ\r\n    If T1 Then\r\n        s = \"Şükrü\"\r\n    End If\r\nEnd Sub\r\n";
        var bytes = enc.GetBytes(src);
        Assert.Contains((byte)0xFE, bytes); // ş = 0xFE (Windows-1254)

        var result = TestPaths.Rewrite(src);
        string expected = "Sub Test()\r\n    ' Açıklama: ğüşıöç İĞÜŞÖÇ\r\n    s = \"Şükrü\"\r\nEnd Sub\r\n";
        Assert.Equal(enc.GetBytes(expected), result.Output);
    }

    [Fact]
    public void Lf_endings_and_missing_final_newline_are_preserved()
    {
        string src = "Sub Test()\n    If F1 Then\n        a = 1\n    End If\n    If T1 Then a = 2\nEnd Sub";
        Assert.Equal("Sub Test()\n    a = 2\nEnd Sub", TestPaths.RewriteText(src));
    }

    [Fact]
    public void Unchanged_file_is_returned_byte_for_byte()
    {
        var bytes = File.ReadAllBytes(Path.Combine(TestPaths.SamplesDir, "frmRapor.frm"));
        var file = VbSourceFile.FromBytes("frmRapor.frm", bytes);
        var result = new Rewriter(TestPaths.CaseFlags().Flags).Rewrite(file);
        Assert.False(result.Changed);
        Assert.Equal(bytes, result.Output);
    }

    [Fact]
    public void Unchanged_lines_keep_original_bytes_even_if_not_round_trippable()
    {
        // 0x81 Windows-1254'te tanımsız; değişmeyen satırda aynen kalmalı
        var enc = VbSourceFile.GetEncoding(1254);
        var bytes = new List<byte>();
        bytes.AddRange(enc.GetBytes("Sub Test()\r\n    s = \""));
        bytes.Add(0x81);
        bytes.AddRange(enc.GetBytes("\"\r\n    If F1 Then\r\n        a = 1\r\n    End If\r\nEnd Sub\r\n"));
        var file = VbSourceFile.FromBytes("t.bas", bytes.ToArray());
        var result = new Rewriter(TestPaths.CaseFlags().Flags).Rewrite(file);
        var expected = new List<byte>();
        expected.AddRange(enc.GetBytes("Sub Test()\r\n    s = \""));
        expected.Add(0x81);
        expected.AddRange(enc.GetBytes("\"\r\nEnd Sub\r\n"));
        Assert.Equal(expected.ToArray(), result.Output);
    }

    [Fact]
    public void Logical_lines_join_continuations()
    {
        var file = VbSourceFile.FromText("t.bas", "a = 1 + _\r\n    2 + _\r\n    3\r\nb = 2\r\n");
        var lines = file.GetLogicalLines();
        Assert.Equal(2, lines.Count);
        Assert.Equal("a = 1 + 2 + 3", lines[0].Text);
        Assert.Equal(3, lines[0].Count);
    }

    [Fact]
    public void Lexer_handles_strings_comments_and_rem()
    {
        var t = Lexer.Tokenize("x = \"a 'b\"\" c\" ' yorum");
        Assert.Equal(TokenKind.String, t[2].Kind);
        Assert.Equal("\"a 'b\"\" c\"", t[2].Text);
        Assert.Equal(TokenKind.Comment, t[3].Kind);

        var r = Lexer.Tokenize("x = 1: Rem yorum: If");
        Assert.Equal(TokenKind.Comment, r[^1].Kind);
        Assert.DoesNotContain(r, k => k.Is("If"));
    }
}
