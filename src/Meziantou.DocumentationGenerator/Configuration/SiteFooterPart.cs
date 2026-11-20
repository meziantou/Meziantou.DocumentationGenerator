namespace Meziantou.DocumentationGenerator.Configuration;

/// <summary>Part of the footer: plain text, or a link when <see cref="Url"/> is set.</summary>
internal sealed record SiteFooterPart(string Text, string? Url);
