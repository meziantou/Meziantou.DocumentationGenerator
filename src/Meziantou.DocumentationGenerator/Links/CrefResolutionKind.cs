namespace Meziantou.DocumentationGenerator.Links;

internal enum CrefResolutionKind
{
    /// <summary>The symbol has a page in the site.</summary>
    Resolved,

    /// <summary>The symbol is documented by a configured external site.</summary>
    External,

    /// <summary>Several symbols match at the same priority level; no link is generated.</summary>
    Ambiguous,

    /// <summary>No page or external mapping is known; no link is generated.</summary>
    Unresolved,
}
