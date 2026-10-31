namespace VbFlagResolver.Core;

public enum DiffKind { Same, Removed, Added }

/// <summary>Fark satırı. OldNo/NewNo 1 tabanlıdır; ilgili tarafta yoksa 0.</summary>
public readonly record struct DiffLine(DiffKind Kind, string Text, int OldNo, int NewNo);

/// <summary>Satır tabanlı fark (Myers O(ND)). Önizleme için orijinal ve yeni dosyayı karşılaştırır.</summary>
public static class LineDiff
{
    public static string[] SplitLines(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        return text.EndsWith('\n') || text.EndsWith('\r') ? lines[..^1] : lines;
    }

    public static List<DiffLine> Compute(IReadOnlyList<string> a, IReadOnlyList<string> b, int maxEdits = 5000)
    {
        // ortak baş ve son kısım
        int pre = 0;
        while (pre < a.Count && pre < b.Count && a[pre] == b[pre]) pre++;
        int suf = 0;
        while (suf < a.Count - pre && suf < b.Count - pre && a[a.Count - 1 - suf] == b[b.Count - 1 - suf]) suf++;

        var ops = new List<DiffKind>();
        for (int i = 0; i < pre; i++) ops.Add(DiffKind.Same);
        ops.AddRange(Myers(a, b, pre, a.Count - suf, pre, b.Count - suf, maxEdits));
        for (int i = 0; i < suf; i++) ops.Add(DiffKind.Same);

        var result = new List<DiffLine>(ops.Count);
        int x = 0, y = 0;
        foreach (var op in ops)
        {
            switch (op)
            {
                case DiffKind.Same: result.Add(new DiffLine(op, a[x], ++x, ++y)); break;
                case DiffKind.Removed: result.Add(new DiffLine(op, a[x], ++x, 0)); break;
                default: result.Add(new DiffLine(op, b[y], 0, ++y)); break;
            }
        }
        return result;
    }

    private static List<DiffKind> Myers(IReadOnlyList<string> a, IReadOnlyList<string> b, int a0, int a1, int b0, int b1, int maxEdits)
    {
        int n = a1 - a0, m = b1 - b0, max = n + m;
        var ops = new List<DiffKind>();
        if (max == 0) return ops;
        int off = max + 1;
        var v = new int[2 * max + 3];
        var trace = new List<int[]>();
        int found = -1;
        for (int d = 0; d <= max && d <= maxEdits; d++)
        {
            trace.Add((int[])v.Clone());
            for (int k = -d; k <= d; k += 2)
            {
                int x = k == -d || (k != d && v[k - 1 + off] < v[k + 1 + off]) ? v[k + 1 + off] : v[k - 1 + off] + 1;
                int y = x - k;
                while (x < n && y < m && a[a0 + x] == b[b0 + y]) { x++; y++; }
                v[k + off] = x;
                if (x >= n && y >= m) { found = d; break; }
            }
            if (found >= 0) break;
        }

        if (found < 0)
        {
            // çok farklı: tamamı silindi + eklendi
            for (int i = 0; i < n; i++) ops.Add(DiffKind.Removed);
            for (int i = 0; i < m; i++) ops.Add(DiffKind.Added);
            return ops;
        }

        int cx = n, cy = m;
        for (int d = found; d >= 0; d--)
        {
            var vd = trace[d];
            int k = cx - cy;
            int prevK = k == -d || (k != d && vd[k - 1 + off] < vd[k + 1 + off]) ? k + 1 : k - 1;
            int prevX = vd[prevK + off], prevY = prevX - prevK;
            while (cx > prevX && cy > prevY) { ops.Add(DiffKind.Same); cx--; cy--; }
            if (d > 0)
            {
                if (cx == prevX) { ops.Add(DiffKind.Added); cy--; }
                else { ops.Add(DiffKind.Removed); cx--; }
            }
        }
        ops.Reverse();
        return ops;
    }

    /// <summary>Değişiklik çevresinde <paramref name="context"/> satır bağlamla parçalar.</summary>
    public static List<List<DiffLine>> Hunks(List<DiffLine> diff, int context = 3)
    {
        var hunks = new List<List<DiffLine>>();
        var changed = new List<int>();
        for (int i = 0; i < diff.Count; i++)
            if (diff[i].Kind != DiffKind.Same) changed.Add(i);
        int idx = 0;
        while (idx < changed.Count)
        {
            int start = Math.Max(0, changed[idx] - context);
            int end = Math.Min(diff.Count - 1, changed[idx] + context);
            idx++;
            while (idx < changed.Count && changed[idx] - context <= end + 1)
            {
                end = Math.Min(diff.Count - 1, changed[idx] + context);
                idx++;
            }
            hunks.Add(diff.GetRange(start, end - start + 1));
        }
        return hunks;
    }
}
