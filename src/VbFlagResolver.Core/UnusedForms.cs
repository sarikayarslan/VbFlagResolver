using System.Text;
using System.Text.RegularExpressions;

namespace VbFlagResolver.Core;

/// <summary>Projede hiçbir yerden kullanılmayan formları bulur (kullanılmayan formlardan çağrılanlar da kullanılmaz sayılır).</summary>
public static class UnusedForms
{
    private sealed class Info
    {
        public required string Rel { get; init; }
        public required VbSourceFile File { get; init; }
        public HashSet<string> Idents { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(string Text, int Line)> Strings { get; } = [];
    }

    /// <param name="sources">(göreli yol, dosya içeriği: temizlenmişse çıktı baytları, değilse orijinal)</param>
    /// <param name="sourceDir">Göreli yolların kökü (.vbp'deki Form= yollarını çözmek için).</param>
    /// <param name="protectedRels">Hiçbir zaman çıkarılmayacak ve kök sayılacak kaynaklar (ör. olduğu gibi aktarılan formlar).</param>
    public static (List<RemovedForm> Removed, List<SourceNote> Notes) Find(
        IReadOnlyList<(string Rel, VbSourceFile File)> sources, IReadOnlyList<(string Rel, byte[] Bytes)> vbpFiles, int codePage,
        string sourceDir, ISet<string>? protectedRels = null)
    {
        protectedRels ??= new HashSet<string>();
        var infos = new List<Info>();
        foreach (var (rel, file) in sources)
        {
            var info = new Info { Rel = rel, File = file };
            foreach (var line in file.GetLogicalLines())
                foreach (var t in line.Tokens)
                {
                    if (t.Kind == TokenKind.Identifier) info.Idents.Add(t.Text);
                    else if (t.Kind == TokenKind.String) info.Strings.Add((t.Text, line.LineNumber));
                }
            infos.Add(info);
        }

        string NameOf(Info i) => string.IsNullOrWhiteSpace(i.File.ModuleName)
            ? Path.GetFileNameWithoutExtension(i.Rel) : i.File.ModuleName;
        var forms = infos.Where(i => Path.GetExtension(i.Rel).Equals(".frm", StringComparison.OrdinalIgnoreCase)).ToList();

        // başlangıç formları ve .vbp'lerde listelenen formlar (yalnızca bunlar çıkarılabilir)
        var startup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // tam yollar
        var enc = VbSourceFile.GetEncoding(codePage);
        foreach (var (vbpRel, bytes) in vbpFiles)
        {
            string vbpDir = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(sourceDir, vbpRel)))!;
            foreach (var raw in enc.GetString(bytes).Split(["\r\n", "\r", "\n"], StringSplitOptions.None))
            {
                var line = raw.Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line[..eq].Trim(), value = line[(eq + 1)..].Trim().Trim('"');
                if (key.Equals("Startup", StringComparison.OrdinalIgnoreCase)) startup.Add(value);
                else if (key.Equals("Form", StringComparison.OrdinalIgnoreCase))
                    try { listed.Add(Path.GetFullPath(Path.Combine(vbpDir, value))); } catch (ArgumentException) { }
            }
        }
        bool IsCandidate(Info f) => !protectedRels.Contains(f.Rel) && listed.Contains(Path.GetFullPath(Path.Combine(sourceDir, f.Rel)));

        // MDI
        bool IsHeader(Info i, Func<string, bool> pred) => i.File.Lines.Take(i.File.HeaderLineCount).Any(l => pred(l.Text.Trim()));
        var mdiParent = forms.Where(f => IsHeader(f, s => s.StartsWith("Begin VB.MDIForm", StringComparison.OrdinalIgnoreCase))).ToHashSet();
        var mdiChild = forms.Where(f => IsHeader(f, s =>
        {
            // VB satırı yorumla yazar: "MDIChild        =   -1  'True"
            int eq = s.IndexOf('=');
            if (eq <= 0 || !s[..eq].Trim().Equals("MDIChild", StringComparison.OrdinalIgnoreCase)) return false;
            var value = s[(eq + 1)..];
            int quote = value.IndexOf('\'');
            if (quote >= 0) value = value[..quote];
            return value.Trim() is "-1" || value.Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
        })).ToHashSet();

        var notes = new List<SourceNote>();
        if (vbpFiles.Count == 0)
        {
            // başlangıç formu bilinmeden "kullanılmıyor" kararı verilemez
            notes.Add(new SourceNote("", 0, "Kaynak klasörde .vbp bulunamadı; kullanılmayan form analizi yapılmadı, hiçbir form çıkarılmadı"));
            return ([], notes);
        }

        // Ulaşılabilirlik: kökler form olmayan tüm modüller (.bas/.cls/.ctl, bayrak dosyası) ve başlangıç formu.
        // Canlı bir kaynaktan adıyla (veya string içinde) geçen form canlı olur. Yalnızca birbirini çağıran
        // kullanılmayan formlar (A ↔ B) böylece canlı kalmaz.
        var formSet = forms.ToHashSet();
        // .vbp'de listelenmeyen veya korunan formlar da çıkarılmaz ve kök sayılır
        var alive = new HashSet<Info>(infos.Where(i => !formSet.Contains(i) || startup.Contains(NameOf(i)) || !IsCandidate(i)));
        var noted = new HashSet<Info>();
        var regex = forms.ToDictionary(f => f, f => new Regex($@"\b{Regex.Escape(NameOf(f))}\b", RegexOptions.IgnoreCase));
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var f in forms)
            {
                if (alive.Contains(f)) continue;
                var name = NameOf(f);
                bool used = alive.Any(s => s != f && s.Idents.Contains(name));
                if (!used)
                {
                    foreach (var s in alive)
                    {
                        if (s == f) continue;
                        var hit = s.Strings.FirstOrDefault(x => regex[f].IsMatch(x.Text));
                        if (hit.Text == null) continue;
                        used = true;
                        if (noted.Add(f))
                            notes.Add(new SourceNote(s.Rel, hit.Line,
                                $"{name} adı bir string içinde geçiyor (ör. Forms koleksiyonu); kullanıldığı varsayılıp projede bırakıldı"));
                        break;
                    }
                }
                if (!used && mdiParent.Contains(f) && mdiChild.Any(alive.Contains)) used = true;
                if (!used) continue;
                alive.Add(f);
                changed = true;
            }
        }
        var removedInfo = forms.Where(f => !alive.Contains(f)).ToList();

        var removed = new List<RemovedForm>();
        foreach (var f in forms.Where(removedInfo.Contains))
        {
            var name = NameOf(f);
            bool referenced = infos.Any(s => s != f && s.Idents.Contains(name));
            removed.Add(new RemovedForm(f.Rel, name, referenced ? "yalnızca kullanılmayan formlardan çağrılıyor" : "kullanılmıyor"));
        }
        return (removed, notes);
    }
}
