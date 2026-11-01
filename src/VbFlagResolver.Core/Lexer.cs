namespace VbFlagResolver.Core;

public enum TokenKind
{
    Identifier,
    Number,
    String,
    Date,
    Operator,
    Comment,
    Directive, // #If, #Const vb. — satır başında '#'
}

public sealed record Token(TokenKind Kind, string Text, int Start)
{
    public int End => Start + Text.Length;

    public bool Is(string word) => (Kind == TokenKind.Identifier || Kind == TokenKind.Operator)
                                   && Text.Equals(word, StringComparison.OrdinalIgnoreCase);

    public override string ToString() => $"{Kind}:{Text}";
}

/// <summary>Tek bir mantıksal satırı token'lara ayırır. Boşluklar atlanır; konumlar korunur.</summary>
public static class Lexer
{
    private static readonly string[] TwoCharOps = ["<>", "<=", ">=", "=<", "=>"];

    public static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        int i = 0, n = text.Length;
        bool statementStart = true;
        while (i < n)
        {
            char c = text[i];
            if (c == ' ' || c == '\t') { i++; continue; }

            if (c == '\'')
            {
                tokens.Add(new Token(TokenKind.Comment, text[i..], i));
                break;
            }

            if (c == '"')
            {
                int j = i + 1;
                while (j < n)
                {
                    if (text[j] == '"')
                    {
                        if (j + 1 < n && text[j + 1] == '"') { j += 2; continue; }
                        j++;
                        break;
                    }
                    j++;
                }
                if (j > n) j = n;
                tokens.Add(new Token(TokenKind.String, text[i..j], i));
                i = j;
                statementStart = false;
                continue;
            }

            if (c == '#' && tokens.Count == 0)
            {
                // derleyici yönergesi: #If, #ElseIf, #Else, #End If, #Const
                int j = i + 1;
                while (j < n && IsIdentChar(text[j])) j++;
                if (j > i + 1)
                {
                    tokens.Add(new Token(TokenKind.Directive, text[i..j], i));
                    i = j;
                    statementStart = false;
                    continue;
                }
            }

            if (c == '#')
            {
                // tarih sabiti #1/1/2000#
                int close = text.IndexOf('#', i + 1);
                if (close > i + 1 && LooksLikeDate(text.AsSpan(i + 1, close - i - 1)))
                {
                    tokens.Add(new Token(TokenKind.Date, text[i..(close + 1)], i));
                    i = close + 1;
                    statementStart = false;
                    continue;
                }
            }

            if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(text[i + 1])))
            {
                int j = i;
                while (j < n && (char.IsLetterOrDigit(text[j]) || text[j] == '.')) j++;
                // üs: 1E+10
                if (j < n && (text[j] == '+' || text[j] == '-') && j > i && (text[j - 1] == 'E' || text[j - 1] == 'e') && !text[i..j].Contains('&'))
                {
                    j++;
                    while (j < n && char.IsDigit(text[j])) j++;
                }
                while (j < n && "%&!#@".Contains(text[j])) j++;
                tokens.Add(new Token(TokenKind.Number, text[i..j], i));
                i = j;
                statementStart = false;
                continue;
            }

            if (c == '&' && i + 1 < n && (text[i + 1] == 'H' || text[i + 1] == 'h' || text[i + 1] == 'O' || text[i + 1] == 'o')
                && i + 2 < n && Uri.IsHexDigit(text[i + 2]))
            {
                int j = i + 2;
                while (j < n && Uri.IsHexDigit(text[j])) j++;
                while (j < n && "%&".Contains(text[j])) j++;
                tokens.Add(new Token(TokenKind.Number, text[i..j], i));
                i = j;
                statementStart = false;
                continue;
            }

            if (char.IsLetter(c) || c == '_' || c == '[')
            {
                int j;
                if (c == '[')
                {
                    j = text.IndexOf(']', i);
                    j = j < 0 ? n : j + 1;
                }
                else
                {
                    j = i;
                    while (j < n && IsIdentChar(text[j])) j++;
                    // tip karakteri: Name$, Count%, ...
                    if (j < n && "%&!#@$".Contains(text[j]) && (j + 1 >= n || !IsIdentChar(text[j + 1]))) j++;
                }
                string word = text[i..j];
                if (statementStart && word.Equals("Rem", StringComparison.OrdinalIgnoreCase) && (j >= n || text[j] == ' ' || text[j] == '\t'))
                {
                    tokens.Add(new Token(TokenKind.Comment, text[i..], i));
                    break;
                }
                tokens.Add(new Token(TokenKind.Identifier, word, i));
                i = j;
                statementStart = false;
                continue;
            }

            string? op = null;
            if (i + 1 < n)
            {
                var two = text.Substring(i, 2);
                if (TwoCharOps.Contains(two)) op = two;
            }
            op ??= c.ToString();
            tokens.Add(new Token(TokenKind.Operator, op, i));
            i += op.Length;
            // ':' yeni ifade başlatır (Rem için); "Then"/"Else" sonrası da ifade başlar
            statementStart = op == ":";
        }

        // "Then Rem ..." ve "Else Rem ..." durumları: Rem ifade başında yorumdur
        for (int k = 0; k < tokens.Count - 1; k++)
        {
            if ((tokens[k].Is("Then") || tokens[k].Is("Else")) && tokens[k + 1].Is("Rem"))
            {
                int start = tokens[k + 1].Start;
                tokens.RemoveRange(k + 1, tokens.Count - k - 1);
                tokens.Add(new Token(TokenKind.Comment, text[start..], start));
                break;
            }
        }
        return tokens;
    }

    public static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    // #1/1/2000#, #2000-01-31 14:30:00#, #14:30#, #2:30 PM#, #Jan 1, 2000#, #1 January 2000#
    private static readonly System.Text.RegularExpressions.Regex DateLiteral = new(
        @"^\s*(?:(?:\d{1,4}[/\-.]\d{1,2}(?:[/\-.]\d{1,4})?|[A-Za-z]{3,9}\s+\d{1,2},?\s+\d{2,4}|\d{1,2}\s+[A-Za-z]{3,9},?\s+\d{2,4})" +
        @"(?:\s+\d{1,2}(?::\d{2}){1,2}(?:\s*[AaPp][Mm])?)?|\d{1,2}(?::\d{2}){1,2}(?:\s*[AaPp][Mm])?|\d{1,2}\s*[AaPp][Mm])\s*$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Tarih/saat sabiti mi? Gevşek kontrol "Close #1: If x Then Close #2" satırındaki "#1: ... #" kısmını
    /// tarih sanıyordu; bu yüzden gerçek tarih/saat biçimi aranır.
    /// </summary>
    private static bool LooksLikeDate(ReadOnlySpan<char> s) => DateLiteral.IsMatch(s);

    /// <summary>Satırdaki token'ları üst düzey ':' ayıracından ifadelere böler (yorum hariç).</summary>
    public static List<List<Token>> SplitStatements(List<Token> tokens)
    {
        var result = new List<List<Token>>();
        var cur = new List<Token>();
        int depth = 0;
        foreach (var t in tokens)
        {
            if (t.Kind == TokenKind.Comment) break;
            if (t.Kind == TokenKind.Operator)
            {
                if (t.Text == "(") depth++;
                else if (t.Text == ")") depth--;
                else if (t.Text == ":" && depth == 0)
                {
                    result.Add(cur);
                    cur = [];
                    continue;
                }
            }
            cur.Add(t);
        }
        result.Add(cur);
        return result;
    }

    public static Token? Comment(List<Token> tokens) => tokens.Count > 0 && tokens[^1].Kind == TokenKind.Comment ? tokens[^1] : null;
}
