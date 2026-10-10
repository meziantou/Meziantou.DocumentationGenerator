using System.Collections.Immutable;

namespace Meziantou.DocumentationGenerator.Api;

/// <summary>
/// Target framework folders of the package assets declaring a symbol. This is asset availability, not compatibility:
/// a symbol available in <c>netstandard2.0</c> is not reported as available in <c>net8.0</c>.
/// </summary>
internal sealed record ApiAvailability(ImmutableArray<string> TargetFrameworks, bool IsAvailableInAllTargetFrameworks)
{
    public bool Equals(ApiAvailability? other) => other is not null && IsAvailableInAllTargetFrameworks == other.IsAvailableInAllTargetFrameworks && TargetFrameworks.SequenceEqual(other.TargetFrameworks, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(IsAvailableInAllTargetFrameworks, TargetFrameworks.Length);
}
