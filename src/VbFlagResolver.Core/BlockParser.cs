namespace VbFlagResolver.Core;

public enum LineKind { Other, BlockIf, ElseIf, Else, EndIf, SingleIf }

/// <summary>Sınıflandırılmış mantıksal satır.</summary>
public sealed class ClassifiedLine
{
    public required LogicalLine Line { get; init; }
    public required LineKind Kind { get; init; }
    /// <summary>
    /// Blok satırı tek başına mı (etiket, ':' ile ek ifade yok)? Temiz olmayan başlıklar değiştirilmez.
    /// </summary>
    public bool Clean { get; init; } = true;
    /// <summary>If/ElseIf anahtar kelimesinin token indeksi.</summary>
    public int KeywordIndex { get; init; } = -1;
    /// <summary>Then token indeksi (If/ElseIf/SingleIf).</summary>
    public int ThenIndex { get; init; } = -1;
    /// <summary>"Else If b Then" (boşluklu): Else satırı iç içe bir blok If de açar; If'in token indeksi.</summary>
    public int NestedIfIndex { get; init; } = -1;
    /// <summary>Başka bir satırın (Else If) parçası olan sanal başlık: çıktıya yazılmaz.</summary>
    public bool Virtual { get; init; }

    public List<Token> Tokens => Line.Tokens;
    public Token? Comment => Lexer.Comment(Line.Tokens);

    /// <summary>Koşul token'ları (If/ElseIf ile Then arası).</summary>
    public List<Token> ConditionTokens => KeywordIndex >= 0 && ThenIndex > KeywordIndex
        ? Tokens.GetRange(KeywordIndex + 1, ThenIndex - KeywordIndex - 1)
        : [];
}

public abstract class Node;

public sealed class LineNode(ClassifiedLine line) : Node
{
    public ClassifiedLine Line { get; } = line;
}

public sealed class Branch
{
    public required ClassifiedLine Header { get; init; }
    public List<Node> Body { get; } = [];
    public LineKind Kind => Header.Kind;
}

public sealed class IfBlock : Node
{
    public List<Branch> Branches { get; } = [];
    public ClassifiedLine? End { get; set; }
}

public sealed class BlockParseException(string message, int line) : Exception(message)
{
    public int Line { get; } = line;
}

/// <summary>Mantıksal satırları If/ElseIf/Else/End If ağacına çevirir (sınırsız iç içe).</summary>
public static class BlockParser
{
    public static ClassifiedLine Classify(LogicalLine line)
    {
        var tokens = line.Tokens;
        int n = tokens.Count;
        if (n > 0 && tokens[^1].Kind == TokenKind.Comment) n--;
        if (n == 0 || tokens[0].Kind == TokenKind.Directive) return new ClassifiedLine { Line = line, Kind = LineKind.Other };

        // satır başı etiketi: "Hata:" veya satır numarası "100 "
        int k = 0;
        bool label = false;
        if (tokens[0].Kind == TokenKind.Number) { k = 1; label = true; }
        else if (n >= 2 && tokens[0].Kind == TokenKind.Identifier && tokens[1].Text == ":" && !IsKeyword(tokens[0])) { k = 2; label = true; }
        if (k >= n) return new ClassifiedLine { Line = line, Kind = LineKind.Other };

        var first = tokens[k];
        var statements = Lexer.SplitStatements(tokens.GetRange(k, tokens.Count - k));
        bool single = statements.Count(s => s.Count > 0) == 1 && statements.Count == 1;

        if (first.Is("If"))
        {
            int then = FindThen(tokens, k + 1, n);
            if (then < 0) return new ClassifiedLine { Line = line, Kind = LineKind.Other };
            var kind = then == n - 1 ? LineKind.BlockIf : LineKind.SingleIf;
            return new ClassifiedLine
            {
                Line = line, Kind = kind, KeywordIndex = k, ThenIndex = then,
                Clean = !label && (kind == LineKind.SingleIf || single),
            };
        }
        if (first.Is("ElseIf"))
        {
            int then = FindThen(tokens, k + 1, n);
            if (then < 0) return new ClassifiedLine { Line = line, Kind = LineKind.Other };
            return new ClassifiedLine
            {
                Line = line, Kind = LineKind.ElseIf, KeywordIndex = k, ThenIndex = then,
                Clean = !label && then == n - 1,
            };
        }
        if (first.Is("Else"))
        {
            // "Else If b Then": VB'de Else + iç içe blok If (ayrıca bir End If gerekir); ikisi de değiştirilmez
            bool opens = k + 1 < n && tokens[k + 1].Is("If") && FindThen(tokens, k + 2, n) == n - 1;
            return new ClassifiedLine { Line = line, Kind = LineKind.Else, Clean = !label && n - k == 1, NestedIfIndex = opens ? k + 1 : -1 };
        }
        if (first.Is("End") && k + 1 < n && tokens[k + 1].Is("If"))
            return new ClassifiedLine { Line = line, Kind = LineKind.EndIf, Clean = !label && n - k == 2 };
        if (first.Is("EndIf"))
            return new ClassifiedLine { Line = line, Kind = LineKind.EndIf, Clean = !label && n - k == 1 };

        // "x = 1: If a Then y = 2" — ':' sonrasında başlayan If
        int depth = 0;
        for (int i = k; i < n; i++)
        {
            var t = tokens[i];
            if (t.Text == "(") depth++;
            else if (t.Text == ")") depth--;
            else if (depth == 0 && t.Is("If") && i > k && tokens[i - 1].Text == ":")
            {
                int then = FindThen(tokens, i + 1, n);
                if (then < 0) break;
                // blok If ':' sonrasında başlıyorsa yapı korunur ama değiştirilmez
                var kind = then == n - 1 ? LineKind.BlockIf : LineKind.SingleIf;
                return new ClassifiedLine { Line = line, Kind = kind, KeywordIndex = i, ThenIndex = then, Clean = kind == LineKind.SingleIf };
            }
            else if (t.Is("Then")) break;
        }

        return new ClassifiedLine { Line = line, Kind = LineKind.Other };
    }

    private static bool IsKeyword(Token t) => t.Is("Else") || t.Is("End") || t.Is("If");

    private static int FindThen(List<Token> tokens, int from, int to)
    {
        for (int i = from; i < to; i++)
            if (tokens[i].Is("Then")) return i;
        return -1;
    }

    public static List<Node> Parse(IReadOnlyList<LogicalLine> lines)
    {
        var root = new List<Node>();
        var stack = new Stack<IfBlock>();
        List<Node> Current() => stack.Count == 0 ? root : stack.Peek().Branches[^1].Body;

        foreach (var l in lines)
        {
            var c = Classify(l);
            switch (c.Kind)
            {
                case LineKind.BlockIf:
                {
                    var blk = new IfBlock();
                    blk.Branches.Add(new Branch { Header = c });
                    Current().Add(blk);
                    stack.Push(blk);
                    break;
                }
                case LineKind.ElseIf:
                case LineKind.Else:
                {
                    if (stack.Count == 0) throw new BlockParseException($"Eşleşmeyen {(c.Kind == LineKind.Else ? "Else" : "ElseIf")}", l.LineNumber);
                    var blk = stack.Peek();
                    if (blk.Branches[^1].Kind == LineKind.Else) throw new BlockParseException("Else'den sonra ElseIf/Else", l.LineNumber);
                    blk.Branches.Add(new Branch { Header = c });
                    if (c.NestedIfIndex >= 0)
                    {
                        var inner = new IfBlock();
                        inner.Branches.Add(new Branch
                        {
                            Header = new ClassifiedLine
                            {
                                Line = l, Kind = LineKind.BlockIf, KeywordIndex = c.NestedIfIndex,
                                ThenIndex = FindThen(l.Tokens, c.NestedIfIndex + 1, l.Tokens.Count), Clean = false, Virtual = true,
                            },
                        });
                        blk.Branches[^1].Body.Add(inner);
                        stack.Push(inner);
                    }
                    break;
                }
                case LineKind.EndIf:
                {
                    if (stack.Count == 0) throw new BlockParseException("Eşleşmeyen End If", l.LineNumber);
                    stack.Pop().End = c;
                    break;
                }
                default:
                    Current().Add(new LineNode(c));
                    break;
            }
        }
        if (stack.Count > 0)
            throw new BlockParseException("End If eksik", stack.Peek().Branches[0].Header.Line.LineNumber);
        return root;
    }

    /// <summary>Düğümdeki tüm mantıksal satırlar (kaynak sırasıyla).</summary>
    public static IEnumerable<ClassifiedLine> AllLines(Node node)
    {
        switch (node)
        {
            case LineNode ln:
                yield return ln.Line;
                break;
            case IfBlock blk:
                foreach (var b in blk.Branches)
                {
                    if (!b.Header.Virtual) yield return b.Header;
                    foreach (var child in b.Body)
                    foreach (var x in AllLines(child))
                        yield return x;
                }
                if (blk.End != null) yield return blk.End;
                break;
        }
    }
}
