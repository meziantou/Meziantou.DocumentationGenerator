using Meziantou.DocumentationGenerator.Links;

namespace Meziantou.DocumentationGenerator.Content;

/// <param name="DisplayPath">Path used in diagnostics, relative to the configuration directory or prefixed by the package identity.</param>
/// <param name="Content">Markdown text.</param>
/// <param name="LinkContext">Context used to resolve <c>cref:</c> links.</param>
/// <param name="Resolver">Resolves relative links.</param>
internal sealed record MarkdownSource(string DisplayPath, string Content, LinkContext LinkContext, IMarkdownLinkResolver Resolver);
