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
        /// <summary>Tanımlayıcı → ilk geçtiği satır.</summary>
        public Dictionary<string, int> Idents { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(string Text, int Line)> Strings { get; } = [];
    }

    /// <param name="sources">(göreli yol, dosya içeriği: temizlenmişse çıktı baytları, değilse orijinal)</param>
    /// <param name="sourceDir">Göreli yolların kökü (.vbp'deki Form= yollarını çözmek için).</param>
    /// <param name="protectedRels">Hiçbir zaman çıkarılmayacak ve kök sayılacak kaynaklar (ör. olduğu gibi aktarılan formlar).</param>
    /// <param name="cleanedRels">Temizlenmiş içerikle verilen kaynaklar (satır numaraları çıktıdaki yeni satırlardır).</param>
    public static (List<RemovedForm> Removed, List<KeptForm> Kept, List<SourceNote> Notes) Find(
        IReadOnlyList<(string Rel, VbSourceFile File)> sources, IReadOnlyList<(string Rel, byte[] Bytes)> vbpFiles, int codePage,
        string sourceDir, ISet<string>? protectedRels = null, ISet<string>? cleanedRels = null)
    {
        protectedRels ??= new HashSet<string>();
        cleanedRels ??= new HashSet<string>();
        var infos = new List<Info>();
        foreach (var (rel, file) in sources)
        {
            var info = new Info { Rel = rel, File = file };
            foreach (var line in file.GetLogicalLines())
                foreach (var t in line.Tokens)
                {
                    if (t.Kind == TokenKind.Identifier) info.Idents.TryAdd(t.Text, line.LineNumber);
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
            return ([], [], notes);
        }

        // Ulaşılabilirlik: kökler form olmayan tüm modüller (.bas/.cls/.ctl, bayrak dosyası) ve başlangıç formu.
        // Canlı bir kaynaktan adıyla (veya string içinde) geçen form canlı olur. Yalnızca birbirini çağıran
        // kullanılmayan formlar (A ↔ B) böylece canlı kalmaz.
        var formSet = forms.ToHashSet();
        // hiçbir .vbp'de listelenmeyen formlar projenin parçası değildir: çıkarılır ve içlerindeki kullanımlar sayılmaz
        var unlisted = forms.Where(f => !protectedRels.Contains(f.Rel) && !Path.IsPathRooted(f.Rel) && !startup.Contains(NameOf(f))
                                        && !listed.Contains(Path.GetFullPath(Path.Combine(sourceDir, f.Rel)))).ToHashSet();
        // korunan (olduğu gibi aktarılan, işlenmeyen, Lib) formlar çıkarılmaz ve kök sayılır
        var alive = new HashSet<Info>(infos.Where(i => !formSet.Contains(i) || startup.Contains(NameOf(i)) || protectedRels.Contains(i.Rel)));
        var noted = new HashSet<Info>();
        var regex = forms.ToDictionary(f => f, f => new Regex($@"\b{Regex.Escape(NameOf(f))}\b", RegexOptions.IgnoreCase));
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var f in forms)
            {
                if (alive.Contains(f) || unlisted.Contains(f)) continue;
                var name = NameOf(f);
                bool used = alive.Any(s => s != f && s.Idents.ContainsKey(name));
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
            bool referenced = infos.Any(s => s != f && s.Idents.ContainsKey(name));
            removed.Add(new RemovedForm(f.Rel, name,
                unlisted.Contains(f) ? "hiçbir .vbp'de listelenmiyor (projede zaten tanımlı değildi)"
                : referenced ? "yalnızca kullanılmayan formlardan çağrılıyor" : "kullanılmıyor"));
        }

        // projede kalan formlar ve nedeni (nerede kullanıldığı); referans (Lib) klasöründeki formlar listelenmez
        string Where(Info s, int line) => $"{s.Rel} satır {line}{(cleanedRels.Contains(s.Rel) ? " (çıktıdaki satır)" : "")}";
        var kept = new List<KeptForm>();
        foreach (var f in forms.Where(f => alive.Contains(f) && !Path.IsPathRooted(f.Rel)))
        {
            var name = NameOf(f);
            string reason;
            // form olmayan modüllerdeki kullanımlar önce gösterilir
            var refs = alive.Where(s => s != f && s.Idents.ContainsKey(name))
                .OrderBy(s => formSet.Contains(s)).ThenBy(s => s.Rel, StringComparer.OrdinalIgnoreCase).ToList();
            if (startup.Contains(name)) reason = ".vbp başlangıç formu (Startup)";
            else if (protectedRels.Contains(f.Rel)) reason = "olduğu gibi aktarılıyor veya işlenmiyor; çıkarılmaz";
            else if (refs.Count > 0)
                reason = $"kullanıldığı yer: {Where(refs[0], refs[0].Idents[name])}" + (refs.Count > 1 ? $" (+{refs.Count - 1} dosyada daha)" : "");
            else if (alive.FirstOrDefault(s => s != f && s.Strings.Any(x => regex[f].IsMatch(x.Text))) is { } str)
                reason = $"adı string içinde geçiyor: {Where(str, str.Strings.First(x => regex[f].IsMatch(x.Text)).Line)}";
            else if (mdiParent.Contains(f)) reason = "MDI ana formu; kullanılan bir MDI alt formu var";
            else reason = "kullanılıyor";
            kept.Add(new KeptForm(f.Rel, name, reason));
        }
        return (removed, kept, notes);
    }
}
