using System;
using System.Linq;
using System.Reflection;
using Xunit;
class Program
{
    static int Main(string[] args)
    {
        string? filter = args.Length > 0 ? args[0] : null;
        int pass = 0, fail = 0;
        foreach (var t in Assembly.GetExecutingAssembly().GetTypes().Where(t => t.Namespace == "ePrescriptions.Tests"))
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (m.GetCustomAttribute<FactAttribute>() == null) continue;
            if (filter != null && !(t.Name + "." + m.Name).Contains(filter)) continue;
            var sets = m.GetCustomAttributes<InlineDataAttribute>().Select(a => a.Data).ToList();
            if (sets.Count == 0) sets.Add(new object?[0]);
            foreach (var d in sets)
            {
                string name = t.Name + "." + m.Name + (d.Length > 0 ? "(" + string.Join(",", d) + ")" : "");
                try
                {
                    var inst = Activator.CreateInstance(t)!;
                    var ps = m.GetParameters();
                    var conv = d.Select((x, i) => x == null ? null : Convert.ChangeType(x, Nullable.GetUnderlyingType(ps[i].ParameterType) ?? ps[i].ParameterType)).ToArray();
                    m.Invoke(inst, conv);
                    (inst as IDisposable)?.Dispose();
                    pass++; if (filter != null) Console.WriteLine("PASS " + name);
                }
                catch (Exception e) { fail++; var ie = e is TargetInvocationException ? e.InnerException! : e; Console.WriteLine("FAIL " + name + " :: " + ie.GetType().Name + ": " + ie.Message); }
            }
        }
        Console.WriteLine($"TOTAL passed={pass} failed={fail}");
        return fail == 0 ? 0 : 1;
    }
}
