using VbFlagResolver.Core;

namespace VbFlagResolver.Tests;

public class WarningTests
{
    [Fact]
    public void Removed_label_used_elsewhere_is_reported()
    {
        var r = TestPaths.Rewrite("""
            Sub Test()
                On Error GoTo Hata
                If F1 Then
            Hata:
                    MsgBox "x"
                End If
                Resume Hata
            End Sub
            """);
        Assert.Contains(r.Report.Warnings, w => w.Message.Contains("'Hata'"));
    }

    [Fact]
    public void Removed_dim_used_elsewhere_is_reported()
    {
        var r = TestPaths.Rewrite("""
            Sub Test()
                If F1 Then
                    Dim lSayi As Long, sAd As String
                End If
                lSayi = 5
            End Sub
            """);
        Assert.Contains(r.Report.Warnings, w => w.Message.Contains("'lSayi'"));
        Assert.DoesNotContain(r.Report.Warnings, w => w.Message.Contains("'sAd'"));
    }

    [Fact]
    public void Removed_on_error_is_reported()
    {
        var r = TestPaths.Rewrite("""
            Sub Test()
                If F1 Then
                    On Error Resume Next
                End If
            End Sub
            """);
        Assert.Contains(r.Report.Warnings, w => w.Message.Contains("On Error"));
    }

    [Fact]
    public void Dropped_function_call_is_reported()
    {
        var r = TestPaths.Rewrite("""
            Sub Test()
                If F1 And Kaydet() Then
                    a = 1
                End If
            End Sub
            """);
        Assert.Contains(r.Report.Warnings, w => w.Message.Contains("Kaydet()"));
    }

    [Fact]
    public void Flag_used_outside_condition_is_reported()
    {
        var r = TestPaths.Rewrite("""
            Sub Test()
                x = T1
            End Sub
            """);
        Assert.False(r.Changed);
        Assert.Contains(r.Report.Warnings, w => w.Message.Contains("koşul dışında"));
    }

    [Fact]
    public void Unbalanced_blocks_leave_file_unchanged()
    {
        var r = TestPaths.Rewrite("""
            Sub Test()
                If T1 Then
                    a = 1
            End Sub
            """);
        Assert.True(r.Report.Failed);
        Assert.False(r.Changed);
    }
}
