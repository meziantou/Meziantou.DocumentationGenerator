using Meziantou.DocumentationGenerator.Tests.Infrastructure;

namespace Meziantou.DocumentationGenerator.Tests.EndToEnd;

/// <summary>
/// Representative packages built from source in memory: multi-targeting with Source Link, a reference-assembly package
/// depending on it, a dependency-only package and an undocumented package.
/// </summary>
internal static class FixturePackages
{
    private const string ExtensionsSource = """
        namespace Contoso.Extensions
        {
            /// <summary>Extends <see cref="Contoso.Library.Client"/>.</summary>
            public class SmartClient : Contoso.Library.Base
            {
                /// <inheritdoc />
                public override void Run() { }

                /// <summary>Sends using <see cref="Contoso.Library.Client.Send(string)"/>.</summary>
                /// <param name="text">The text.</param>
                public void Send(string text) { }
            }
        }

        namespace Utils
        {
            /// <summary>Guard of the extensions package.</summary>
            public static class Guard
            {
                /// <summary>Checks.</summary>
                public static void NotNull(object value) { }
            }
        }
        """;

    private const string UndocumentedSource = """
        namespace Utils
        {
            public static class Guard
            {
                public static void NotNull(object value) { }
            }
        }
        """;

    public static IReadOnlyList<TestPackageBuilder> Create(bool reverseEntries = false)
    {
        var sourceLink = SourceLinkTests.GitHubSourceLink;
        var library = TestCompiler.Compile("Contoso.Library", [("/_/src/Library.cs", TestFixtures.KitchenSink)], new CompileOptions { SourceLink = sourceLink, EmbeddedPdb = true });
        var libraryNet8 = TestCompiler.Compile("Contoso.Library", [("/_/src/Library.cs", TestFixtures.KitchenSink)], new CompileOptions { SourceLink = sourceLink, EmbeddedPdb = true, Defines = ["NET8"] });
        var extensions = TestCompiler.Compile("Contoso.Extensions", [("/_/src/Extensions.cs", ExtensionsSource)], new CompileOptions { SourceLink = sourceLink, References = [libraryNet8] });
        var extensionsReference = TestCompiler.Compile("Contoso.Extensions", [("/_/src/Extensions.cs", ExtensionsSource)], new CompileOptions { ReferenceAssembly = true, References = [libraryNet8] });
        var undocumented = TestCompiler.Compile("Contoso.Undocumented", UndocumentedSource);

        var core = new TestPackageBuilder("Contoso.Library", "1.0.0")
            .WithNuspecMetadata($"""<readme>docs/README.md</readme><repository type="git" url="https://github.com/contoso/library" commit="{SourceLinkTests.Commit}" /><license type="expression">MIT</license>""")
            .AddFile("docs/README.md", """
                # Contoso.Library

                ![logo](logo.png)

                Use [`Client`](cref:T:Contoso.Library.Client) to send messages. See [contributing](CONTRIBUTING.md).

                ```csharp
                var client = new Contoso.Library.Client("name");
                client.Send("Hello");
                ```
                """)
            .AddFile("docs/logo.png", [0x89, 0x50, 0x4E, 0x47])
            .AddFile("lib/netstandard2.0/Contoso.Library.dll", library.Assembly)
            .AddFile("lib/netstandard2.0/Contoso.Library.xml", library.Xml)
            .AddFile("lib/net8.0/Contoso.Library.dll", libraryNet8.Assembly)
            .AddFile("lib/net8.0/Contoso.Library.xml", libraryNet8.Xml);

        var extensionsPackage = new TestPackageBuilder("Contoso.Extensions", "1.0.0")
            .WithNuspecMetadata("""<dependencies><group targetFramework="net8.0"><dependency id="Contoso.Library" version="1.0.0" /></group></dependencies>""")
            .AddFile("ref/net8.0/Contoso.Extensions.dll", extensionsReference.Assembly)
            .AddFile("ref/net8.0/Contoso.Extensions.xml", extensions.Xml)
            .AddFile("lib/net8.0/Contoso.Extensions.dll", extensions.Assembly)
            .AddFile("lib/net8.0/Contoso.Extensions.pdb", extensions.Pdb!);

        var meta = new TestPackageBuilder("Contoso.Meta", "1.0.0")
            .WithNuspecMetadata("""<readme>README.md</readme><dependencies><group targetFramework="net8.0"><dependency id="Contoso.Library" version="1.0.0" /></group></dependencies>""")
            .AddFile("README.md", "# Contoso.Meta\n\nA metapackage referencing [Contoso.Library](cref:Contoso.Library!N:Contoso.Library).");

        var undocumentedPackage = new TestPackageBuilder("Contoso.Undocumented", "1.0.0")
            .AddFile("lib/net8.0/Contoso.Undocumented.dll", undocumented.Assembly);

        IReadOnlyList<TestPackageBuilder> packages = [core, extensionsPackage, meta, undocumentedPackage];
        return reverseEntries ? [.. packages.Select(p => p.WithReversedEntries())] : packages;
    }

    public const string Configuration = """
        {
          "$schema": "https://raw.githubusercontent.com/meziantou/Meziantou.DocumentationGenerator/main/schema/docgen.schema.json",
          "version": 1,
          "site": { "title": "Contoso documentation", "url": "https://docs.example.com", "robots": "allow", "footer": "Contoso" },
          "cacheDirectory": "cache",
          "packages": [
            { "id": "Contoso.Library", "version": "1.0.0", "path": "packages/contoso.library.1.0.0.nupkg" },
            { "id": "Contoso.Extensions", "version": "1.0.0", "path": "packages/contoso.extensions.1.0.0.nupkg" },
            { "id": "Contoso.Meta", "version": "1.0.0", "path": "packages/contoso.meta.1.0.0.nupkg" },
            { "id": "Contoso.Undocumented", "version": "1.0.0", "path": "packages/contoso.undocumented.1.0.0.nupkg" }
          ],
          "content": { "root": "docs", "exclude": [ "**/AGENTS.md" ] },
          "xrefs": [ { "namespace": "System", "url": "https://learn.microsoft.com/dotnet/api/{uid}" } ]
        }
        """;

    public static readonly Dictionary<string, string> Content = new()
    {
        // Shaped like meziantou.framework/docs: numbered folders and files, _folder.yml, images, an excluded AGENTS.md
        ["docs/index.md"] = "# Contoso documentation\n\nStart with the [guides](01-Guides/) or the [API](cref:T:Contoso.Library.Client).",
        ["docs/AGENTS.md"] = "Instructions for agents, not a page.",
        ["docs/01-Guides/_folder.yml"] = "title: Guides\ndescription: Install and use Contoso.",
        ["docs/01-Guides/01-GettingStarted.md"] = "---\ntitle: Getting started\ndescription: Install and use Contoso.\n---\n## Install\n\n```bash\ndotnet add package Contoso.Library\n```\n\n![diagram](images/diagram.png)\n\n## Next\n\nSee [extensions](02-Extensions.md#usage).",
        ["docs/01-Guides/02-Extensions.md"] = "# Extensions\n\n## Usage\n\nUse [](cref:T:Contoso.Extensions.SmartClient) and [](cref:Contoso.Extensions!T:Utils.Guard). Back to [getting started](01-GettingStarted.md#install).",
        ["docs/01-Guides/images/diagram.png"] = "png",
    };
}
