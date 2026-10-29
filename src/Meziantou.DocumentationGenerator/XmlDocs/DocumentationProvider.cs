using System.Collections.Concurrent;
using System.Collections.Immutable;
using Meziantou.DocumentationGenerator.Api;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Packages;
using Meziantou.Framework.PublicApiGenerator;

namespace Meziantou.DocumentationGenerator.XmlDocs;

/// <summary>
/// Associates documentation with symbols. Each target framework only uses the XML file of its own assembly, so
/// documentation is never borrowed from another framework or another assembly declaring the same IDs.
/// </summary>
internal sealed class DocumentationProvider(DiagnosticBag diagnostics)
{
    private readonly Dictionary<ApiAssemblyModel, XmlDocumentationFile?> _files = [];
    private readonly ConcurrentDictionary<(ApiAssemblyModel Model, string DocumentationId), DocumentationComment> _comments = new();

    public static DocumentationProvider Empty { get; } = new(new DiagnosticBag());

    public void Load(ApiCatalog catalog, CancellationToken cancellationToken)
    {
        foreach (var group in catalog.Assemblies)
        {
            using var archive = PackageArchive.Open(group.Package.Package.Path);
            foreach (var model in group.Models)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // The documentation of a source package is produced by the compiler from its sources
                var entry = model.Asset.CompiledSource is not null ? model.Asset.AssemblyEntry : model.Asset.XmlDocumentationEntry;
                if (entry is null)
                {
                    _files[model] = null;
                    continue;
                }

                var displayPath = group.Package.Package.Configuration.Identity + "/" + entry;
                var location = new DiagnosticLocation { Package = group.Package.Package.Configuration.Identity, TargetFramework = model.TargetFramework, Assembly = model.Asset.AssemblyEntry };
                using var stream = model.Asset.CompiledSource is { } compiled ? new MemoryStream(compiled.XmlDocumentation, writable: false) : archive.OpenRead(entry);
                var file = XmlDocumentationFile.Load(stream, displayPath, diagnostics, location);
                _files[model] = file;
                if (file is not null)
                {
                    ReportUnmatchedEntries(model, file, location);
                }
            }
        }
    }

    public XmlDocumentationFile? GetFile(ApiAssemblyModel model) => _files.GetValueOrDefault(model);

    /// <summary>Returns the documentation of a symbol for the target framework of <paramref name="model"/>.</summary>
    public DocumentationComment GetComment(ApiAssemblyModel model, PublicApiSymbol symbol)
    {
        return _comments.GetOrAdd((model, symbol.DocumentationId), key =>
        {
            var file = GetFile(key.Model);
            var member = file?.Find(key.DocumentationId);
            return member is null ? DocumentationComment.Empty with { Scope = key.Model.Scope } : DocumentationCommentParser.Parse(member, symbol, key.Model.Scope, file!.DisplayPath, diagnostics);
        });
    }

    /// <summary>Replaces the documentation of a symbol, after inherited documentation and includes are resolved.</summary>
    public void SetComment(ApiAssemblyModel model, string documentationId, DocumentationComment comment) => _comments[(model, documentationId)] = comment;

    /// <summary>Documentation of each target framework of the symbol, grouping frameworks with identical documentation.</summary>
    public ImmutableArray<DocumentationVariant> GetVariants(ApiSymbolModel symbol)
    {
        var groups = new List<(string Key, List<string> TargetFrameworks, DocumentationComment Comment)>();
        foreach (var targetFramework in symbol.Availability.TargetFrameworks)
        {
            var frameworkSymbol = symbol.Aggregate.GetSymbol(targetFramework);
            if (frameworkSymbol is null)
                continue;

            var comment = GetComment(symbol.Assembly.GetModel(targetFramework), frameworkSymbol);
            var key = comment.GetComparisonKey();
            var existing = groups.FindIndex(g => string.Equals(g.Key, key, StringComparison.Ordinal));
            if (existing >= 0)
            {
                groups[existing].TargetFrameworks.Add(targetFramework);
            }
            else
            {
                groups.Add((key, [targetFramework], comment));
            }
        }

        return [.. groups.Select(g => new DocumentationVariant([.. g.TargetFrameworks], g.Comment))];
    }

    private void ReportUnmatchedEntries(ApiAssemblyModel model, XmlDocumentationFile file, DiagnosticLocation location)
    {
        var unmatched = file.MemberIds
            .Where(id => !id.StartsWith("N:", StringComparison.Ordinal) && model.Assembly.FindSymbolByDocumentationId(id) is null)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (unmatched.Count > 0)
        {
            // Documentation of internal members is expected; it is reported for diagnosis only
            diagnostics.Report(DiagnosticDescriptors.UnmatchedXmlDocumentation, $"{unmatched.Count} documentation entries of '{file.DisplayPath}' do not match a public symbol, such as '{unmatched[0]}'", location);
        }
    }
}
