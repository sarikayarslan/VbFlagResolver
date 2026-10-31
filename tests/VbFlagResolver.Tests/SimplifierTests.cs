using VbFlagResolver.Core;

namespace VbFlagResolver.Tests;

public class SimplifierTests
{
    private static readonly FlagTable Flags = FlagTable.FromValues(("T", true), ("F", false), ("U", null));

    private static SimplifyResult S(string expr) => new Simplifier(Flags).Simplify(ExprParser.Parse(expr));

    [Theory]
    [InlineData("T", Tri.True)]
    [InlineData("F", Tri.False)]
    [InlineData("Not F", Tri.True)]
    [InlineData("T And F", Tri.False)]
    [InlineData("T Or F", Tri.True)]
    [InlineData("T Xor T", Tri.False)]
    [InlineData("T Eqv F", Tri.False)]
    [InlineData("F Imp F", Tri.True)]
    [InlineData("F And x", Tri.False)]
    [InlineData("T Or f(x)", Tri.True)]
    [InlineData("T = True", Tri.True)]
    [InlineData("F <> False", Tri.False)]
    [InlineData("(T)", Tri.True)]
    [InlineData("Not (T And Not F)", Tri.False)]
    [InlineData("t", Tri.True)] // büyük/küçük harf duyarsız
    public void Evaluates_known_values(string expr, Tri expected) => Assert.Equal(expected, S(expr).Value);

    [Theory]
    [InlineData("T And x", "x")]
    [InlineData("x And T", "x")]
    [InlineData("F Or x > 3", "x > 3")]
    [InlineData("T Xor x", "Not x")]
    [InlineData("T Xor x > 3", "Not (x > 3)")]
    [InlineData("F Eqv x", "Not x")]
    [InlineData("T Eqv x", "x")]
    [InlineData("T Imp x", "x")]
    [InlineData("x Imp F", "Not x")]
    [InlineData("Not (T And x)", "Not x")]
    [InlineData("Not (T And Not x)", "x")]
    [InlineData("(T And x) Or y", "x Or y")]
    [InlineData("(T And (x Or y)) And z", "(x Or y) And z")]
    [InlineData("U = True And T", "U")]
    [InlineData("U = False And T", "Not U")]
    [InlineData("U <> True And T", "Not U")]
    [InlineData("U <> False And T", "U")]
    [InlineData("True = U And T", "U")]
    [InlineData("x = True And T", "x = True")] // x Boolean olduğu bilinmiyor
    [InlineData("T = x", "True = x")]
    [InlineData("a + b > 2 And T", "a + b > 2")]
    [InlineData("T And (a Or b)", "(a Or b)")]
    [InlineData("T And Not x", "Not x")]
    [InlineData("T And obj.Visible", "obj.Visible")]
    public void Simplifies_unknown_parts(string expr, string expected)
    {
        var r = S(expr);
        Assert.Equal(Tri.Unknown, r.Value);
        Assert.True(r.Changed);
        Assert.Equal(expected, r.Text);
    }

    [Theory]
    [InlineData("x > 0")]
    [InlineData("U")]
    [InlineData("False")]
    [InlineData("x = True")]
    [InlineData("Not Not x")]
    public void Leaves_expressions_without_known_flags_untouched(string expr)
    {
        var r = S(expr);
        Assert.Equal(Tri.Unknown, r.Value);
        Assert.False(r.Changed);
        Assert.Equal(expr, r.Text);
    }

    [Fact]
    public void Reports_dropped_function_calls()
    {
        Assert.Equal(["Kaydet()"], S("F And Kaydet()").DroppedCalls);
        Assert.Equal(["obj.Kontrol(1)"], S("T Or obj.Kontrol(1)").DroppedCalls);
        Assert.Empty(S("T And Kaydet()").DroppedCalls);
    }

    [Theory]
    [InlineData("a Or b And c", "Binary(Or")]
    [InlineData("Not a = b", "NotExpr")]
    [InlineData("a Xor b Or c", "Binary(Xor")]
    [InlineData("a Eqv b Xor c", "Binary(Eqv")]
    public void Parser_respects_precedence(string expr, string rootStartsWith)
    {
        var e = ExprParser.Parse(expr);
        var desc = e switch { Binary b => $"Binary({b.Op}", NotExpr => "NotExpr", _ => e.GetType().Name };
        Assert.StartsWith(rootStartsWith, desc);
    }
}
