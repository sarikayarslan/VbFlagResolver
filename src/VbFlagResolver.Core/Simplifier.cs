namespace VbFlagResolver.Core;

/// <summary>Üç değerli mantık değeri.</summary>
public enum Tri { False, True, Unknown }

public sealed class SimplifyResult
{
    public required Tri Value { get; init; }
    /// <summary>Sadeleşmiş ifade (Value Unknown ise anlamlı).</summary>
    public required Expr Residual { get; init; }
    /// <summary>İfade orijinalinden farklı mı (en az bir bilinen bayrak işlendi mi)?</summary>
    public required bool Changed { get; init; }
    /// <summary>Değerlendirmede kullanılan (bilinen) bayraklar.</summary>
    public required IReadOnlyCollection<string> KnownFlagsUsed { get; init; }
    /// <summary>Sadeleştirmede atılan ama VB'de yine de çalışacak olan (kısa devre yok) olası çağrılar.</summary>
    public required IReadOnlyList<string> DroppedCalls { get; init; }

    public string Text => ExprPrinter.Print(Residual);
}

/// <summary>
/// Üç değerli mantıkla kısmi değerlendirme. VB'nin And/Or/Xor/Eqv/Imp/Not operatörleri bit düzeyindedir;
/// burada kullanılan özdeşlikler (-1 And x = x, 0 Or x = x, -1 Xor x = Not x, ...) her tamsayı x için geçerlidir.
/// "= True" gibi karşılaştırmalar ise yalnızca Boolean olduğu bilinen ifadelerde sadeleştirilir.
/// </summary>
public sealed class Simplifier(FlagTable flags)
{
    private readonly record struct R(Tri V, Expr E);

    private HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _dropped = [];

    public SimplifyResult Simplify(Expr e)
    {
        _used = new(StringComparer.OrdinalIgnoreCase);
        _dropped = [];
        var r = S(e);
        bool changed = _used.Count > 0;
        if (!changed)
        {
            // bilinen bayrak yoksa ifadeye dokunma ("If False Then" gibi elle yazılmış bloklar da korunur)
            return new SimplifyResult { Value = Tri.Unknown, Residual = e, Changed = false, KnownFlagsUsed = [], DroppedCalls = [] };
        }
        return new SimplifyResult { Value = r.V, Residual = r.E, Changed = true, KnownFlagsUsed = _used, DroppedCalls = _dropped };
    }

    /// <summary>Değeri hesaplar; Simplify'dan farklı olarak bayraksız ifadeleri de ("True Or False") değerlendirir.</summary>
    public Tri Evaluate(Expr e)
    {
        _used = new(StringComparer.OrdinalIgnoreCase);
        _dropped = [];
        return S(e).V;
    }

    private static R Known(bool v) => new(v ? Tri.True : Tri.False, new BoolLit(v));
    private static R Unk(Expr e) => new(Tri.Unknown, e);

    private R S(Expr e)
    {
        switch (e)
        {
            case BoolLit b:
                return new(b.Value ? Tri.True : Tri.False, e);
            case Ident i:
                if (flags.TryGetKnown(i.Name, out bool v))
                {
                    _used.Add(i.Name);
                    return Known(v);
                }
                return Unk(e);
            case Opaque:
                return Unk(e);
            case Paren p:
            {
                var r = S(p.Inner);
                if (r.V != Tri.Unknown) return Known(r.V == Tri.True);
                if (ReferenceEquals(r.E, p.Inner)) return Unk(p);
                return Unk(IsAtom(r.E) ? r.E : new Paren(r.E));
            }
            case NotExpr n:
            {
                var r = S(n.Operand);
                if (r.V != Tri.Unknown) return Known(r.V != Tri.True);
                if (ReferenceEquals(r.E, n.Operand)) return Unk(n);
                return Unk(Negate(r.E));
            }
            case Compare c:
                return SimplifyCompare(c);
            case Binary b:
                return SimplifyBinary(b);
            default:
                throw new InvalidOperationException(e.GetType().Name);
        }
    }

    private static bool IsAtom(Expr e) => e switch
    {
        Ident or BoolLit or Paren => true,
        Opaque o => !o.Text.Contains(' '),
        _ => false,
    };

    /// <summary>Not x; x zaten Not ise çift olumsuzluk kaldırılır (bit düzeyinde de geçerli).</summary>
    public static Expr Negate(Expr e) => e switch
    {
        NotExpr n => n.Operand,
        Paren { Inner: NotExpr n } => n.Operand,
        _ => new NotExpr(e),
    };

    /// <summary>İfadenin Boolean (-1/0) değer ürettiği biliniyor mu?</summary>
    public bool IsBooleanTyped(Expr e) => e switch
    {
        BoolLit => true,
        Ident i => flags.Get(i.Name) is { NonBoolean: false, Shadowed: false },
        Compare => true,
        NotExpr n => IsBooleanTyped(n.Operand),
        Paren p => IsBooleanTyped(p.Inner),
        Binary b => IsBooleanTyped(b.Left) && IsBooleanTyped(b.Right),
        _ => false,
    };

    private R SimplifyCompare(Compare c)
    {
        var l = S(c.Left);
        var r = S(c.Right);
        bool lk = l.V != Tri.Unknown, rk = r.V != Tri.Unknown;

        if (lk && rk && c.Op is "=" or "<>" or "<" or ">" or "<=" or ">=" or "=<" or "=>")
        {
            int a = l.V == Tri.True ? -1 : 0, b = r.V == Tri.True ? -1 : 0;
            bool res = c.Op switch
            {
                "=" => a == b,
                "<>" => a != b,
                "<" => a < b,
                ">" => a > b,
                "<=" or "=<" => a <= b,
                ">=" or "=>" => a >= b,
                _ => throw new InvalidOperationException(c.Op),
            };
            return Known(res);
        }

        if (lk != rk && c.Op is "=" or "<>")
        {
            var known = lk ? l : r;
            var other = lk ? r : l;
            if (IsBooleanTyped(other.E))
            {
                bool positive = (c.Op == "=") == (known.V == Tri.True); // = True, <> False
                return Unk(positive ? other.E : Negate(other.E));
            }
        }

        if (ReferenceEquals(l.E, c.Left) && ReferenceEquals(r.E, c.Right)) return Unk(c);
        return Unk(new Compare(c.Op, l.E, r.E));
    }

    private R SimplifyBinary(Binary b)
    {
        var l = S(b.Left);
        var r = S(b.Right);
        bool lk = l.V != Tri.Unknown, rk = r.V != Tri.Unknown;
        bool lv = l.V == Tri.True, rv = r.V == Tri.True;

        if (lk && rk)
        {
            return Known(b.Op switch
            {
                LogicOp.And => lv && rv,
                LogicOp.Or => lv || rv,
                LogicOp.Xor => lv ^ rv,
                LogicOp.Eqv => lv == rv,
                LogicOp.Imp => !lv || rv,
                _ => throw new InvalidOperationException(),
            });
        }

        if (lk || rk)
        {
            bool kv = lk ? lv : rv;
            var other = lk ? r : l;
            switch (b.Op)
            {
                case LogicOp.And:
                    if (!kv) { Drop(other.E); return Known(false); }
                    return other;
                case LogicOp.Or:
                    if (kv) { Drop(other.E); return Known(true); }
                    return other;
                case LogicOp.Xor:
                    return kv ? Unk(Negate(other.E)) : other;
                case LogicOp.Eqv:
                    return kv ? other : Unk(Negate(other.E));
                case LogicOp.Imp:
                    if (lk) // False Imp x = True, True Imp x = x
                    {
                        if (!kv) { Drop(other.E); return Known(true); }
                        return other;
                    }
                    // x Imp True = True, x Imp False = Not x
                    if (kv) { Drop(other.E); return Known(true); }
                    return Unk(Negate(other.E));
            }
        }

        if (ReferenceEquals(l.E, b.Left) && ReferenceEquals(r.E, b.Right)) return Unk(b);
        return Unk(new Binary(b.Op, l.E, r.E));
    }

    /// <summary>Atılan alt ifadedeki olası fonksiyon çağrılarını kaydeder (VB'de kısa devre yoktur).</summary>
    private void Drop(Expr e)
    {
        foreach (var o in Opaques(e))
            if (o.HasCall) _dropped.Add(o.Text);
    }

    private static IEnumerable<Opaque> Opaques(Expr e) => e switch
    {
        Opaque o => [o],
        NotExpr n => Opaques(n.Operand),
        Paren p => Opaques(p.Inner),
        Compare c => Opaques(c.Left).Concat(Opaques(c.Right)),
        Binary b => Opaques(b.Left).Concat(Opaques(b.Right)),
        _ => [],
    };
}
