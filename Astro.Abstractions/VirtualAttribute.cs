namespace Astro.Abstractions;

/// <summary>
/// Specify that this class is allowed to be inherited.
/// </summary>
/// <remarks>
/// An analyzer (ASTR0005) prevents accidental usage of non-sealed classes:
/// a class must be either marked [Virtual], abstract, static, or sealed.
/// </remarks>
/// <example>
/// <code>
/// // Warning ASTR0005: Class must be explicitly marked as [Virtual], abstract, static, or sealed.
/// public class MyVirtualClass;
///
/// // No warning.
/// [Virtual]
/// public class MyBetterVirtualClass;
///
/// // Also no warnings:
/// public sealed class MyClass1;
/// public static class MyClass2;
/// public abstract class MyClass3;
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class VirtualAttribute : Attribute;
