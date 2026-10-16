using System;
using System.Collections;
using System.Linq;
namespace FluentAssertions
{
    public static class Ext { public static Subject<T> Should<T>(this T s) => new Subject<T>(s); }
    public class Fail : Exception { public Fail(string m) : base(m) { } }
    public class Subject<T>
    {
        readonly T? _s; public Subject(T? s) { _s = s; }
        public Subject<T> Which => this;
        static bool Eq(object? a, object? b)
        {
            if (a == null || b == null) return a == b;
            if (a is IConvertible && b is IConvertible && !(a is string) && !(b is string) && !(a is Enum) && !(b is Enum))
                try { return Convert.ToDecimal(a) == Convert.ToDecimal(b); } catch { }
            if (a is Enum && b is IConvertible && !(b is Enum)) return Convert.ToInt64(a) == Convert.ToInt64(b);
            return a.Equals(b);
        }
        static string D(object? o) => o is string s ? "\"" + s + "\"" : (o?.ToString() ?? "null");
        public Subject<T> Be(object? e, string because = "", params object[] a) { if (!Eq(_s, e)) throw new Fail($"Expected {D(e)} but found {D(_s)} {because}"); return this; }
        public Subject<T> NotBe(object? e, string because = "") { if (Eq(_s, e)) throw new Fail($"Did not expect {D(e)} {because}"); return this; }
        public Subject<T> BeTrue(string because = "") { if (!(_s is bool b && b)) throw new Fail("Expected true " + because); return this; }
        public Subject<T> BeFalse(string because = "") { if (!(_s is bool b && !b)) throw new Fail("Expected false " + because); return this; }
        public Subject<T> BeNull(string because = "") { if (_s != null) throw new Fail("Expected null"); return this; }
        public Subject<T> NotBeNull(string because = "") { if (_s == null) throw new Fail("Expected not null " + because); return this; }
        public Subject<T> BeEmpty(string because = "") { if (_s is string s ? s.Length != 0 : (_s as IEnumerable)!.Cast<object>().Any()) throw new Fail("Expected empty but was " + D(_s)); return this; }
        public Subject<T> NotBeEmpty(string because = "") { if (_s is string s ? s.Length == 0 : !(_s as IEnumerable)!.Cast<object>().Any()) throw new Fail("Expected not empty"); return this; }
        public Subject<T> NotBeNullOrEmpty(string because = "") { if (string.IsNullOrEmpty(_s as string)) throw new Fail("Expected not null/empty"); return this; }
        public Subject<T> Contain(object? e, string because = "") { bool ok = _s is string s ? s.Contains((string)e!) : (_s as IEnumerable)!.Cast<object>().Any(x => Eq(x, e)); if (!ok) throw new Fail($"Expected {D(_s)} to contain {D(e)} {because}"); return this; }
        public Subject<T> NotContain(object? e, string because = "") { bool ok = _s is string s ? s.Contains((string)e!) : (_s as IEnumerable)!.Cast<object>().Any(x => Eq(x, e)); if (ok) throw new Fail($"Expected {D(_s)} not to contain {D(e)}"); return this; }
        public Subject<T> Contain(Func<dynamic, bool> pred) { if (!(_s as IEnumerable)!.Cast<object>().Any(x => pred(x))) throw new Fail("No element matches predicate"); return this; }
        public Subject<T> NotContain(Func<dynamic, bool> pred) { if ((_s as IEnumerable)!.Cast<object>().Any(x => pred(x))) throw new Fail("Element matches predicate"); return this; }
        public Subject<T> HaveCount(int n, string because = "") { int c = (_s as IEnumerable)!.Cast<object>().Count(); if (c != n) throw new Fail($"Expected count {n} but was {c} {because}"); return this; }
        public Subject<dynamic> ContainSingle(string because = "") { var l = (_s as IEnumerable)!.Cast<object>().ToList(); if (l.Count != 1) throw new Fail($"Expected single but count {l.Count}"); return new Subject<dynamic>(l[0]); }
        public Subject<object> ContainSingle(Func<dynamic, bool> pred) { var l = (_s as IEnumerable)!.Cast<object>().Where(x => pred(x)).ToList(); if (l.Count != 1) throw new Fail($"Expected single match but {l.Count}"); return new Subject<dynamic>(l[0]); }
        public Subject<T> Equal(params object[] e) { var l = (_s as IEnumerable)!.Cast<object>().ToList(); if (l.Count != e.Length || !l.Zip(e, Eq).All(x => x)) throw new Fail("Sequences differ"); return this; }
        int Cmp(object? o) => Convert.ToDecimal(_s).CompareTo(Convert.ToDecimal(o));
        public Subject<T> BeGreaterThan(object o, string because = "") { if (Cmp(o) <= 0) throw new Fail($"Expected > {o} but {_s}"); return this; }
        public Subject<T> BeGreaterThanOrEqualTo(object o, string because = "") { if (Cmp(o) < 0) throw new Fail($"Expected >= {o} but {_s}"); return this; }
        public Subject<T> BeLessThanOrEqualTo(object o, string because = "") { if (Cmp(o) > 0) throw new Fail($"Expected <= {o} but {_s}"); return this; }
        public Subject<T> BeLessThan(object o, string because = "") { if (Cmp(o) >= 0) throw new Fail($"Expected < {o} but {_s}"); return this; }
        public Subject<T> Throw<TEx>(string because = "") where TEx : Exception { try { (_s as Action)!(); } catch (TEx) { return this; } catch (Exception e) { throw new Fail("Wrong exception " + e.GetType().Name); } throw new Fail("No exception"); }
        public Subject<T> BeApproximately(object o, object tol, string because = "") { if (Math.Abs(Convert.ToDouble(_s) - Convert.ToDouble(o)) > Convert.ToDouble(tol)) throw new Fail($"Expected ~{o} but {_s}"); return this; }
    }
}
