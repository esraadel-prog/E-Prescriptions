using System;
namespace Xunit
{
    [AttributeUsage(AttributeTargets.Method)] public class FactAttribute : Attribute { public string? Skip { get; set; } }
    [AttributeUsage(AttributeTargets.Method)] public class TheoryAttribute : FactAttribute { }
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)] public class InlineDataAttribute : Attribute { public object?[] Data; public InlineDataAttribute(params object?[] d) { Data = d; } }
}
