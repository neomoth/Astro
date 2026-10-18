using JetBrains.Annotations;

namespace Astro.Abstractions;

/// <summary>
/// Marks a type as a service provided through IoC.
/// </summary>
/// <remarks>
/// Not scanned for yet: implementations still have to be registered by hand in AstroIoC.
/// Implementations are constructed by the container via reflection, so they're treated as used (like [UsedImplicitly]).
/// </remarks>
[UsedImplicitly(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature, ImplicitUseTargetFlags.WithInheritors)]
public interface IDependency;
