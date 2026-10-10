using Meziantou.Framework.PublicApiGenerator;

namespace Meziantou.DocumentationGenerator.Api;

/// <summary>Formats declarations for display using PublicApiGenerator, without implementation bodies.</summary>
internal static class DeclarationFormatter
{
    /// <summary>Attributes that change how consumers use an API. Other attributes are kept in the model but not displayed.</summary>
    private static readonly HashSet<string> DisplayedAttributes = new(StringComparer.Ordinal)
    {
        "System.ObsoleteAttribute",
        "System.FlagsAttribute",
        "System.AttributeUsageAttribute",
        "System.Diagnostics.CodeAnalysis.ExperimentalAttribute",
        "System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute",
        "System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute",
        "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute",
        "System.Runtime.Versioning.RequiresPreviewFeaturesAttribute",
        "System.Runtime.Versioning.SupportedOSPlatformAttribute",
        "System.Runtime.Versioning.UnsupportedOSPlatformAttribute",
        "System.Runtime.Versioning.ObsoletedOSPlatformAttribute",
        "System.Runtime.CompilerServices.ModuleInitializerAttribute",
    };

    private static readonly PublicApiFormattingOptions Options = new()
    {
        Style = PublicApiDeclarationStyle.Declaration,
        IncludeAttributes = true,
        AttributeFilter = attribute => DisplayedAttributes.Contains(attribute.AttributeType.FullName),
        QualifyTypeNames = false,
        NewLine = "\n",
    };

    public static PublicApiFormattedDeclaration Format(PublicApiSymbol symbol) => PublicApiFormatter.Format(symbol, Options);

    public static PublicApiFormattedDeclaration Format(PublicApiTypeReference type) => PublicApiFormatter.Format(type, Options);

    public static bool IsDisplayedAttribute(PublicApiAttribute attribute) => DisplayedAttributes.Contains(attribute.AttributeType.FullName);
}
