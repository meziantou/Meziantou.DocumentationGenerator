using System.Collections.Immutable;

namespace Meziantou.DocumentationGenerator.SourceLinks;

internal sealed record SourceLocationVariant(ImmutableArray<string> TargetFrameworks, SourceLocation Location);
