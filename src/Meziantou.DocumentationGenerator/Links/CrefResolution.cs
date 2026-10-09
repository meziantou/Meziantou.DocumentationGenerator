using Meziantou.DocumentationGenerator.Api;

namespace Meziantou.DocumentationGenerator.Links;

/// <param name="Kind">Result of the resolution.</param>
/// <param name="Url">URL of the target for resolved and external references.</param>
/// <param name="DisplayName">Short name to display, such as <c>Client.Send</c>.</param>
/// <param name="Symbol">The target symbol when it is part of the configured packages.</param>
/// <param name="Candidates">Descriptions of the candidates of an ambiguous reference.</param>
internal sealed record CrefResolution(CrefResolutionKind Kind, string? Url, string DisplayName, ApiSymbolModel? Symbol, IReadOnlyList<string> Candidates)
{
    public bool HasUrl => Url is not null;
}
