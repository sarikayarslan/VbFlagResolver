using System.Text;
using VbFlagResolver.Core;

namespace VbFlagResolver.Gui;

/// <summary>Fark satırlarını renkli RTF'ye çevirir (RichTextBox'ta satır satır renklendirmekten çok daha hızlı).</summary>
internal static class DiffRtf
{
    // renk tablosu: 1 silinen zemin, 2 eklenen zemin, 3 başlık zemin, 4 gri, 5 koyu kırmızı, 6 koyu yeşil, 7 lacivert
    private const string Header =
        @"{\rtf1\ansi\deff0{\fonttbl{\f0\fmodern Consolas;}}" +
        @"{\colortbl;\red255\green228\blue228;\red220\green248\blue220;\red232\green236\blue248;" +
        @"\red128\green128\blue128;\red160\green0\blue0;\red0\green110\blue0;\red40\green60\blue120;}" +
        @"\f0\fs18 ";

    /// <param name="oldLineAt">Her görüntü satırı için orijinal satır numarası (başlık, boşluk ve eklenen satırlarda 0).</param>
    public static string Build(string original, string output, bool allLines, out List<int> oldLineAt)
    {
        var diff = LineDiff.Compute(LineDiff.SplitLines(original), LineDiff.SplitLines(output));
        var sb = new StringBuilder(Header);
        oldLineAt = [];
        if (diff.All(d => d.Kind == DiffKind.Same))
        {
            sb.Append(@"\cf4 ");
            Escape(sb, "Bu dosyada değişiklik yok.");
            sb.Append(@"\par}");
            oldLineAt.Add(0);
            return sb.ToString();
        }

        IEnumerable<List<DiffLine>> blocks = allLines ? [diff] : LineDiff.Hunks(diff, 3);
        foreach (var hunk in blocks)
        {
            if (!allLines)
            {
                var first = hunk[0];
                sb.Append(@"\highlight3\cf7 ");
                Escape(sb, $"@@ orijinal satır {Math.Max(first.OldNo, 1)}, yeni satır {Math.Max(first.NewNo, 1)} @@");
                sb.Append(@"\highlight0\cf0\par").AppendLine();
                oldLineAt.Add(0);
            }
            foreach (var l in hunk)
            {
                string old = l.OldNo > 0 ? l.OldNo.ToString() : "";
                string nw = l.NewNo > 0 ? l.NewNo.ToString() : "";
                sb.Append(l.Kind switch
                {
                    DiffKind.Removed => @"\highlight1\cf5 ",
                    DiffKind.Added => @"\highlight2\cf6 ",
                    _ => @"\cf4 ",
                });
                Escape(sb, $"{old,5} {nw,5} ");
                if (l.Kind == DiffKind.Same) sb.Append(@"\cf0 ");
                Escape(sb, (l.Kind switch { DiffKind.Removed => "- ", DiffKind.Added => "+ ", _ => "  " }) + l.Text);
                sb.Append(@"\highlight0\cf0\par").AppendLine();
                oldLineAt.Add(l.Kind == DiffKind.Added ? 0 : l.OldNo);
            }
            if (!allLines) { sb.Append(@"\par").AppendLine(); oldLineAt.Add(0); }
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static void Escape(StringBuilder sb, string s)
    {
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append(@"\\"); break;
                case '{': sb.Append(@"\{"); break;
                case '}': sb.Append(@"\}"); break;
                case '\t': sb.Append(@"\tab "); break;
                default:
                    if (c < 128) sb.Append(c);
                    else sb.Append(@"\u").Append((short)c).Append('?');
                    break;
            }
        }
    }
}
