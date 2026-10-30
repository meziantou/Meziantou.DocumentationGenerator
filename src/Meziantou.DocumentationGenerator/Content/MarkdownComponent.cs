namespace Meziantou.DocumentationGenerator.Content;

/// <summary>What a Markdown component is replaced with: a shell command or a link.</summary>
internal sealed record MarkdownComponent(MarkdownComponentKind Kind, string Text, string? Url = null);
