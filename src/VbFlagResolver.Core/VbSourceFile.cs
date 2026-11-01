using System.Text;

namespace VbFlagResolver.Core;

/// <summary>Kaynak dosyadaki tek bir fiziksel satır. Orijinal baytlar korunur.</summary>
public sealed class PhysicalLine
{
    public required string Text { get; init; }
    /// <summary>Satır sonu: "\r\n", "\n", "\r" veya "" (dosyanın son satırı).</summary>
    public required string Ending { get; init; }
    /// <summary>Satır sonu hariç orijinal baytlar.</summary>
    public required byte[] Bytes { get; init; }
    /// <summary>1 tabanlı satır numarası.</summary>
    public required int Number { get; init; }
}

/// <summary>Satır devamı ( _) ile birleşmiş mantıksal satır.</summary>
public sealed class LogicalLine
{
    /// <summary>Kod bölümündeki fiziksel satır listesinde başlangıç indeksi.</summary>
    public required int Start { get; init; }
    public required int Count { get; init; }
    /// <summary>Birleştirilmiş metin (devam karakterleri çıkarılmış).</summary>
    public required string Text { get; init; }
    /// <summary>İlk fiziksel satırın 1 tabanlı numarası (rapor için).</summary>
    public required int LineNumber { get; init; }

    private List<Token>? _tokens;
    public List<Token> Tokens => _tokens ??= Lexer.Tokenize(Text);

    public string Indent
    {
        get
        {
            int i = 0;
            while (i < Text.Length && (Text[i] == ' ' || Text[i] == '\t')) i++;
            return Text[..i];
        }
    }
}

/// <summary>VB5 kaynak dosyası (.frm/.bas/.cls/.ctl): okuma, header ayırma, satır birleştirme, yazma.</summary>
public sealed class VbSourceFile
{
    public string Path { get; }
    public Encoding Encoding { get; }
    public byte[] OriginalBytes { get; }
    public List<PhysicalLine> Lines { get; }
    /// <summary>Header (VERSION/Begin...End/Attribute) satır sayısı; kod bu indeksten başlar.</summary>
    public int HeaderLineCount { get; }
    /// <summary>Dosyada en çok kullanılan satır sonu; yeni üretilen satırlar bunu kullanır.</summary>
    public string DominantEnding { get; }
    /// <summary>Attribute VB_Name değeri (yoksa dosya adı).</summary>
    public string ModuleName { get; }

    public IReadOnlyList<PhysicalLine> CodeLines => Lines.GetRange(HeaderLineCount, Lines.Count - HeaderLineCount);

    static VbSourceFile()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static Encoding GetEncoding(int codePage) => Encoding.GetEncoding(codePage);

    private VbSourceFile(string path, byte[] bytes, Encoding encoding)
    {
        Path = path;
        Encoding = encoding;
        OriginalBytes = bytes;
        Lines = SplitLines(bytes, encoding);
        HeaderLineCount = FindHeaderEnd(Lines);
        DominantEnding = Lines.Where(l => l.Ending != "").GroupBy(l => l.Ending)
            .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? "\r\n";
        ModuleName = FindModuleName(Lines) ?? System.IO.Path.GetFileNameWithoutExtension(path);
    }

    public static VbSourceFile Load(string path, int codePage = 1254)
        => new(path, File.ReadAllBytes(path), GetEncoding(codePage));

    public static VbSourceFile FromBytes(string path, byte[] bytes, int codePage = 1254)
        => new(path, bytes, GetEncoding(codePage));

    public static VbSourceFile FromText(string path, string text, int codePage = 1254)
    {
        var enc = GetEncoding(codePage);
        return new(path, enc.GetBytes(text), enc);
    }

    private static List<PhysicalLine> SplitLines(byte[] bytes, Encoding enc)
    {
        var result = new List<PhysicalLine>();
        int start = 0, number = 1;
        for (int i = 0; i < bytes.Length; i++)
        {
            byte b = bytes[i];
            if (b != (byte)'\r' && b != (byte)'\n') continue;
            string ending = b == (byte)'\r' && i + 1 < bytes.Length && bytes[i + 1] == (byte)'\n' ? "\r\n" : b == (byte)'\r' ? "\r" : "\n";
            var lineBytes = bytes[start..i];
            result.Add(new PhysicalLine { Text = enc.GetString(lineBytes), Ending = ending, Bytes = lineBytes, Number = number++ });
            i += ending.Length - 1;
            start = i + 1;
        }
        if (start < bytes.Length)
        {
            var lineBytes = bytes[start..];
            result.Add(new PhysicalLine { Text = enc.GetString(lineBytes), Ending = "", Bytes = lineBytes, Number = number });
        }
        // UTF-8 BOM (Notepad ekleyebilir): baytlarda kalır, metinden çıkarılır; yoksa ilk satır tanınmaz
        if (result.Count > 0 && bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            result[0] = new PhysicalLine { Text = enc.GetString(result[0].Bytes[3..]), Ending = result[0].Ending, Bytes = result[0].Bytes, Number = 1 };
        return result;
    }

    /// <summary>
    /// VERSION, Object, Begin ... End (iç içe, BeginProperty/EndProperty dahil) ve Attribute satırlarını header sayar.
    /// </summary>
    internal static int FindHeaderEnd(List<PhysicalLine> lines)
    {
        int i = 0, depth = 0;
        while (i < lines.Count)
        {
            string t = lines[i].Text.Trim();
            if (depth > 0)
            {
                if (StartsWithWord(t, "BeginProperty") || StartsWithWord(t, "Begin")) depth++;
                else if (t.Equals("End", StringComparison.OrdinalIgnoreCase) || StartsWithWord(t, "EndProperty")) depth--;
                i++;
                continue;
            }
            if (StartsWithWord(t, "VERSION") || StartsWithWord(t, "Object")) { i++; continue; }
            if (StartsWithWord(t, "Begin")) { depth++; i++; continue; }
            if (StartsWithWord(t, "Attribute")) { i++; continue; }
            break;
        }
        return depth > 0 ? 0 : i;
    }

    private static bool StartsWithWord(string t, string word)
        => t.StartsWith(word, StringComparison.OrdinalIgnoreCase)
           && (t.Length == word.Length || !char.IsLetterOrDigit(t[word.Length]) && t[word.Length] != '_');

    private static string? FindModuleName(List<PhysicalLine> lines)
    {
        foreach (var l in lines)
        {
            var t = l.Text.Trim();
            if (!t.StartsWith("Attribute VB_Name", StringComparison.OrdinalIgnoreCase)) continue;
            int q1 = t.IndexOf('"'), q2 = t.LastIndexOf('"');
            if (q1 >= 0 && q2 > q1) return t[(q1 + 1)..q2];
        }
        return null;
    }

    /// <summary>Fiziksel satır " _" ile bitiyorsa (yorum/string dışında) devam ediyor demektir.</summary>
    public static bool IsContinued(string text)
    {
        var t = text.TrimEnd(' ', '\t');
        if (t.Length == 0 || t[^1] != '_') return false;
        if (t.Length == 1) return true;
        char prev = t[^2];
        return prev == ' ' || prev == '\t';
    }

    /// <summary>Kod bölümünü mantıksal satırlara böler. Start indeksleri kod bölümüne görelidir.</summary>
    public List<LogicalLine> GetLogicalLines() => BuildLogicalLines(CodeLines);

    public static List<LogicalLine> BuildLogicalLines(IReadOnlyList<PhysicalLine> lines)
    {
        var result = new List<LogicalLine>();
        int i = 0;
        while (i < lines.Count)
        {
            int start = i;
            var sb = new StringBuilder();
            while (true)
            {
                // devam satırlarının baştaki boşlukları birleştirmede atılır
                var text = i == start ? lines[i].Text : lines[i].Text.TrimStart(' ', '\t');
                if (IsContinued(text) && i + 1 < lines.Count)
                {
                    var t = text.TrimEnd(' ', '\t');
                    sb.Append(t, 0, t.Length - 1);
                    if (sb.Length > 0 && sb[^1] != ' ' && sb[^1] != '\t') sb.Append(' ');
                    i++;
                    continue;
                }
                sb.Append(text);
                break;
            }
            result.Add(new LogicalLine { Start = start, Count = i - start + 1, Text = sb.ToString(), LineNumber = lines[start].Number });
            i++;
        }
        return result;
    }

    /// <summary>Çıktı satırlarını orijinal kodlamayla bayta çevirir. Değişmeyen satırlar orijinal baytlarını korur.</summary>
    public byte[] Render(IEnumerable<OutputLine> lines)
    {
        using var ms = new MemoryStream();
        foreach (var l in lines)
        {
            var bytes = l.OriginalBytes ?? Encoding.GetBytes(l.Text);
            ms.Write(bytes);
            var end = Encoding.GetBytes(l.Ending);
            ms.Write(end);
        }
        return ms.ToArray();
    }
}

/// <summary>Rewriter çıktısındaki satır. OriginalBytes doluysa satır bayt düzeyinde aynen yazılır.</summary>
public sealed class OutputLine
{
    public required string Text { get; init; }
    public required string Ending { get; init; }
    public byte[]? OriginalBytes { get; init; }

    public static OutputLine From(PhysicalLine p) => new() { Text = p.Text, Ending = p.Ending, OriginalBytes = p.Bytes };
}
