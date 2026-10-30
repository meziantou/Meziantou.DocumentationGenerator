using System.Text;
using Meziantou.DocumentationGenerator.Configuration;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Tests.Infrastructure;
using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public async Task MinimalConfiguration_UsesDefaults()
    {
        await using var temp = TemporaryDirectory.Create();
        var (configuration, diagnostics) = Load(temp, """
            {
              "version": 1,
              "packages": [ { "id": "Contoso.Json", "version": "2.1.0" } ]
            }
            """);

        Assert.Empty(diagnostics);
        Assert.NotNull(configuration);
        Assert.Equal("/", configuration.Site.BasePath.Value);
        Assert.Null(configuration.Site.SiteUrl);
        Assert.Equal("Documentation", configuration.Site.Title);
        Assert.Equal(temp.FullPath / "_site", configuration.OutputDirectory);
        Assert.Equal(RobotsPolicy.None, configuration.Site.Robots);
        var product = Assert.Single(configuration.Products);
        Assert.Equal("contoso.json", product.Id);
        Assert.True(product.IsImplicit);
        Assert.Equal(["Contoso.Json"], product.Packages);
        Assert.Contains("$.site.basePath", configuration.DefaultedValues);
        Assert.Contains("$.output", configuration.DefaultedValues);
        Assert.DoesNotContain("$.packages", configuration.DefaultedValues);
        Assert.Equal(["Microsoft", "System"], configuration.ExternalReferences.Select(x => x.Namespace));
        Assert.All(configuration.ExternalReferences, x => Assert.Equal("https://learn.microsoft.com/dotnet/api/{uid}", x.UrlTemplate));
        Assert.Contains("$.xrefs", configuration.DefaultedValues);
    }

    [Fact]
    public async Task Xrefs_ConfiguredListReplacesTheDefault()
    {
        await using var temp = TemporaryDirectory.Create();
        var (empty, _) = Load(temp, """{ "version": 1, "packages": [ { "id": "A", "version": "1.0.0" } ], "xrefs": [] }""");
        Assert.Empty(empty!.ExternalReferences);

        var (custom, _) = Load(temp, """{ "version": 1, "packages": [ { "id": "A", "version": "1.0.0" } ], "xrefs": [ { "namespace": "Newtonsoft", "url": "https://example.com/{uid}" } ] }""");
        Assert.Equal(["Newtonsoft"], custom!.ExternalReferences.Select(x => x.Namespace));
    }

    [Fact]
    public async Task MultipleProducts_GroupedPackages()
    {
        await using var temp = TemporaryDirectory.Create();
        temp.CreateTextFile("docs/data.md", "# Data");
        var (configuration, diagnostics) = Load(temp, """
            {
              "version": 1,
              "packages": [
                { "id": "Contoso.Data", "version": "3.0.0" },
                { "id": "Contoso.Data.Sqlite", "version": "3.0.0-beta.1" },
                { "id": "Contoso.Json", "version": "2.1.0" },
              ],
              "products": [
                { "id": "data", "name": "Contoso Data", "packages": [ "Contoso.Data", "contoso.data.sqlite" ] },
                { "id": "json", "packages": [ "Contoso.Json" ] },
              ],
            }
            """);

        Assert.Empty(diagnostics);
        Assert.NotNull(configuration);
        Assert.Collection(configuration.Products,
            product =>
            {
                Assert.Equal("data", product.Id);
                Assert.Equal("Contoso Data", product.Name);
                Assert.Equal(["Contoso.Data", "Contoso.Data.Sqlite"], product.Packages);
                Assert.False(product.IsImplicit);
            },
            product =>
            {
                Assert.Equal("json", product.Id);
                Assert.Equal("Contoso.Json", product.Name);
            });
        Assert.Equal("3.0.0-beta.1", configuration.Packages[1].ExactVersion?.ToNormalizedString());
    }

    [Fact]
    public async Task TargetFrameworksAndAssemblyFilters()
    {
        await using var temp = TemporaryDirectory.Create();
        var (configuration, diagnostics) = Load(temp, """
            {
              "version": 1,
              "packages": [ { "id": "Contoso.Json", "version": "2.1.0", "targetFrameworks": [ "net8.0", "netstandard2.0" ], "assemblies": [ "Contoso.Json.dll" ] } ]
            }
            """);

        Assert.Empty(diagnostics);
        Assert.NotNull(configuration);
        Assert.Equal(["net8.0", "netstandard2.0"], configuration.Packages[0].TargetFrameworks.Select(f => f.GetShortFolderName()));
        Assert.Equal(["Contoso.Json.dll"], configuration.Packages[0].Assemblies);
    }

    [Fact]
    public async Task InvalidTargetFramework_ReportsLocation()
    {
        await using var temp = TemporaryDirectory.Create();
        var (configuration, diagnostics) = Load(temp, """
            {
              "version": 1,
              "packages": [
                { "id": "Contoso.Json", "version": "2.1.0", "targetFrameworks": [ "net8.0", "not-a-framework" ] }
              ]
            }
            """);

        Assert.Null(configuration);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("DG1005", diagnostic.Code);
        Assert.Equal("docgen.json", diagnostic.Location?.FilePath);
        Assert.Equal(4, diagnostic.Location?.Line);
        Assert.Equal(81, diagnostic.Location?.Column);
        Assert.Equal("$.packages[0].targetFrameworks[1]", diagnostic.Location?.JsonPath);
    }

    [Fact]
    public async Task ContentAssets()
    {
        await using var temp = TemporaryDirectory.Create();
        temp.CreateTextFile("content/images/logo.png", "png");
        temp.CreateTextFile("content/CNAME", "docs.example.com");
        var (configuration, diagnostics) = Load(temp, """
            {
              "version": 1,
              "packages": [ { "id": "Contoso.Json", "version": "2.1.0" } ],
              "content": { "root": "content", "assets": [ "images", "CNAME" ] }
            }
            """);

        Assert.Empty(diagnostics);
        Assert.NotNull(configuration);
        Assert.NotNull(configuration.Content);
        Assert.Equal(temp.FullPath / "content", configuration.Content.Root);
        Assert.Equal(["images", "CNAME"], configuration.Content.Assets);
    }

    [Fact]
    public async Task ContentEditUrl_EncodesThePathRelativeToTheRoot()
    {
        await using var temp = TemporaryDirectory.Create();
        temp.CreateTextFile("content/01-Guides/Getting started.md", "# Start");
        var (configuration, diagnostics) = Load(temp, """
            {
              "version": 1,
              "content": { "root": "content", "editUrl": "https://github.com/contoso/docs/edit/main/content/{path}" }
            }
            """);

        Assert.Empty(diagnostics);
        Assert.Equal("https://github.com/contoso/docs/edit/main/content/01-Guides/Getting%20started.md", configuration!.Content!.GetEditUrl(temp.FullPath / "content" / "01-Guides" / "Getting started.md"));
    }

    [Theory]
    [InlineData("https://github.com/contoso/docs/edit/main/content/")]
    [InlineData("javascript:alert(1)//{path}")]
    [InlineData("edit/{path}")]
    public async Task ContentEditUrl_Invalid(string editUrl)
    {
        await using var temp = TemporaryDirectory.Create();
        temp.CreateTextFile("content/index.md", "# Home");
        var (_, diagnostics) = Load(temp, $$"""
            {
              "version": 1,
              "content": { "root": "content", "editUrl": "{{editUrl}}" }
            }
            """);

        Assert.Contains(diagnostics, d => d.Code == "DG1013" && d.Severity == DiagnosticSeverity.Error && d.Location?.JsonPath == "$.content.editUrl");
    }

    [Theory]
    [InlineData("""{ "version": 1, "packages": [ { "id": "A", "version": "1.0.0", "readme": "README.md" } ] }""", "$.packages[0].readme")]
    [InlineData("""{ "version": 1, "packages": [ { "id": "A", "version": "1.0.0" } ], "products": [ { "id": "p", "packages": [ "A" ], "landingPage": "docs/index.md" } ] }""", "$.products[0].landingPage")]
    [InlineData("""{ "version": 1, "packages": [ { "id": "A", "version": "1.0.0" } ], "products": [ { "id": "p", "packages": [ "A" ], "readmePackage": "A" } ] }""", "$.products[0].readmePackage")]
    [InlineData("""{ "version": 1, "content": { "root": "docs", "toc": "toc.yml" } }""", "$.content.toc")]
    public async Task RemovedOptions_AreUnknownProperties(string json, string jsonPath)
    {
        await using var temp = TemporaryDirectory.Create();
        temp.CreateTextFile("docs/index.md", "# Home");
        var (configuration, diagnostics) = Load(temp, json);

        Assert.Null(configuration);
        Assert.Equal(jsonPath, Assert.Single(diagnostics, d => d.Code == "DG1002").Location?.JsonPath);
    }

    [Fact]
    public async Task RelativePaths_AreResolvedAgainstConfigurationDirectory()
    {
        await using var temp = TemporaryDirectory.Create();
        temp.CreateTextFile("sub/dir/docs/index.md", "# Home");
        var configurationPath = temp.CreateTextFile("sub/dir/docgen.json", """{ "version": 1, "output": "../out", "content": { "root": "docs" } }""");
        var diagnostics = new DiagnosticBag();

        // The process working directory is not the configuration directory
        Assert.NotEqual(configurationPath.Parent, FullPath.CurrentDirectory());
        var configuration = ConfigurationLoader.Load(configurationPath, diagnostics);

        Assert.Empty(diagnostics.GetDiagnostics());
        Assert.NotNull(configuration);
        Assert.Equal(temp.FullPath / "sub" / "out", configuration.OutputDirectory);
        Assert.Equal(temp.FullPath / "sub" / "dir" / "docs", configuration.Content?.Root);
    }

    [Theory]
    [InlineData(null, "/")]
    [InlineData("/", "/")]
    [InlineData("docs", "/docs/")]
    [InlineData("/docs", "/docs/")]
    [InlineData("/docs/", "/docs/")]
    [InlineData("/a/b/", "/a/b/")]
    public async Task BasePath_IsNormalized(string? basePath, string expected)
    {
        await using var temp = TemporaryDirectory.Create();
        var site = basePath is null ? "" : $$""", "site": { "basePath": "{{basePath}}" }""";
        var (configuration, diagnostics) = Load(temp, $$"""{ "version": 1, "packages": [ { "id": "A", "version": "1.0.0" } ]{{site}} }""");

        Assert.Empty(diagnostics);
        Assert.Equal(expected, configuration?.Site.BasePath.Value);
    }

    [Theory]
    [InlineData("/docs/../x/")]
    [InlineData("https://example.com/docs/")]
    [InlineData("/docs?x=1")]
    [InlineData("/a//b/")]
    public async Task BasePath_Invalid(string basePath)
    {
        await using var temp = TemporaryDirectory.Create();
        var (configuration, diagnostics) = Load(temp, $$"""{ "version": 1, "packages": [ { "id": "A", "version": "1.0.0" } ], "site": { "basePath": "{{basePath}}" } }""");

        Assert.Null(configuration);
        Assert.Equal("DG1006", Assert.Single(diagnostics).Code);
    }

    [Fact]
    public async Task SiteUrl_IsAnOriginCombinedWithBasePath()
    {
        await using var temp = TemporaryDirectory.Create();
        var (configuration, diagnostics) = Load(temp, """{ "version": 1, "packages": [ { "id": "A", "version": "1.0.0" } ], "site": { "url": "https://example.com", "basePath": "/docs/" } }""");

        Assert.Empty(diagnostics);
        Assert.Equal("https://example.com/", configuration?.Site.SiteUrl?.ToString());
        Assert.Equal("/docs/", configuration?.Site.BasePath.Value);
    }

    [Theory]
    [InlineData("https://example.com/docs/")]
    [InlineData("ftp://example.com")]
    [InlineData("example.com")]
    [InlineData("https://example.com/?q=1")]
    public async Task SiteUrl_Invalid(string url)
    {
        await using var temp = TemporaryDirectory.Create();
        var (configuration, diagnostics) = Load(temp, $$"""{ "version": 1, "packages": [ { "id": "A", "version": "1.0.0" } ], "site": { "url": "{{url}}" } }""");

        Assert.Null(configuration);
        Assert.Equal("DG1006", Assert.Single(diagnostics).Code);
    }

    [Fact]
    public async Task Robots_UnderSubpath_Warns()
    {
        await using var temp = TemporaryDirectory.Create();
        var (configuration, diagnostics) = Load(temp, """{ "version": 1, "packages": [ { "id": "A", "version": "1.0.0" } ], "site": { "basePath": "/docs/", "robots": "allow" } }""");

        Assert.NotNull(configuration);
        Assert.Equal("DG1010", Assert.Single(diagnostics).Code);
    }

    [Fact]
    public async Task UnknownProperty_ReportsLocation()
    {
        await using var temp = TemporaryDirectory.Create();
        var (configuration, diagnostics) = Load(temp, """
            {
              "version": 1,
              "packages": [ { "id": "A", "version": "1.0.0", "framework": "net8.0" } ]
            }
            """);

        Assert.Null(configuration);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("DG1002", diagnostic.Code);
        Assert.Equal("$.packages[0].framework", diagnostic.Location?.JsonPath);
        Assert.Equal(3, diagnostic.Location?.Line);
    }

    [Fact]
    public async Task InvalidJson_ReportsLocation()
    {
        await using var temp = TemporaryDirectory.Create();
        var (configuration, diagnostics) = Load(temp, "{\n  \"version\": 1,\n  \"packages\": [ \n}");

        Assert.Null(configuration);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("DG1002", diagnostic.Code);
        Assert.Equal(4, diagnostic.Location?.Line);
    }

    [Theory]
    [InlineData("""{ "packages": [ { "id": "A", "version": "1.0.0" } ] }""", "DG1001")]
    [InlineData("""{ "version": 2, "packages": [ { "id": "A", "version": "1.0.0" } ] }""", "DG1001")]
    [InlineData("""{ "version": 1 }""", "DG1003")]
    [InlineData("""{ "version": 1, "packages": [ { "id": "A", "version": "not-a-version" } ] }""", "DG1004")]
    [InlineData("""{ "version": 1, "packages": [ { "id": "A", "version": "[2.0,1.0]" } ] }""", "DG1004")]
    [InlineData("""{ "version": 1, "packages": [ { "id": "A b", "version": "1.0.0" } ] }""", "DG1004")]
    [InlineData("""{ "version": 1, "packages": [ { "id": "A", "version": "1.0.0" }, { "id": "a", "version": "2.0.0" } ] }""", "DG1008")]
    [InlineData("""{ "version": 1, "packages": [ { "id": "A", "version": "1.0.0", "source": "https://user:secret@example.com/v3/index.json" } ] }""", "DG1011")]
    [InlineData("""{ "version": 1, "packages": [ { "id": "A", "version": "1.0.0" } ], "products": [ { "id": "p", "packages": [ "B" ] } ] }""", "DG1012")]
    [InlineData("""{ "version": 1, "packages": [ { "id": "A", "version": "1.0.0" } ], "products": [ { "id": "Invalid Id", "packages": [ "A" ] } ] }""", "DG1012")]
    [InlineData("""{ "version": 1, "packages": [ { "id": "A", "version": "1.0.0" } ], "products": [ { "id": "p", "packages": [ "A" ] }, { "id": "p", "packages": [ ] } ] }""", "DG1008")]
    [InlineData("""{ "version": 1, "packages": [ { "id": "A", "version": "1.0.0" } ], "output": "." }""", "DG1009")]
    [InlineData("""{ "version": 1, "packages": [ { "id": "A", "version": "1.0.0" } ], "api": { "exclude": [ "X:Foo" ] } }""", "DG1014")]
    [InlineData("""{ "version": 1, "packages": [ { "id": "A", "version": "1.0.0" } ], "diagnostics": { "severity": { "DG9999": "none" } } }""", "DG1015")]
    [InlineData("""{ "version": 1, "packages": [ { "id": "A", "version": "1.0.0" } ], "diagnostics": { "severity": { "DG1009": "warning" } } }""", "DG1015")]
    [InlineData("""{ "version": 1, "packages": [ { "id": "A", "version": "1.0.0" } ], "xrefs": [ { "namespace": "System", "url": "https://learn.microsoft.com/dotnet/api/" } ] }""", "DG1013")]
    [InlineData("""{ "version": 1, "packages": [ { "id": "A", "version": "1.0.0", "path": "missing.nupkg" } ] }""", "DG1007")]
    [InlineData("""{ "version": 1, "packages": [ { "id": "A", "version": "1.0.0", "path": "missing-folder" } ] }""", "DG1007")]
    [InlineData("""{ "version": 1, "packages": [ { "id": "A", "version": "1.0.0", "path": "" } ] }""", "DG1013")]
    public async Task InvalidConfiguration(string json, string expectedCode)
    {
        await using var temp = TemporaryDirectory.Create();
        var (configuration, diagnostics) = Load(temp, json);

        Assert.Null(configuration);
        Assert.Contains(diagnostics, d => d.Code == expectedCode && d.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData(null, "*", null)]
    [InlineData("1.2.3", "1.2.3", "1.2.3")]
    [InlineData("1.2.3-beta.1", "1.2.3-beta.1", "1.2.3-beta.1")]
    [InlineData("[1.2.3]", "[1.2.3]", "1.2.3")]
    [InlineData("*", "*", null)]
    [InlineData("*-*", "*-*", null)]
    [InlineData("1.*", "1.*", null)]
    [InlineData("[1.0,2.0)", "[1.0,2.0)", null)]
    public async Task PackageVersion_ExactRangeOrFloating(string? version, string expectedText, string? expectedExactVersion)
    {
        await using var temp = TemporaryDirectory.Create();
        var versionProperty = version is null ? "" : $$""", "version": "{{version}}" """;
        var (configuration, diagnostics) = Load(temp, $$"""{ "version": 1, "packages": [ { "id": "A"{{versionProperty}} } ] }""");

        Assert.Empty(diagnostics);
        var package = Assert.Single(configuration!.Packages);
        Assert.Equal(expectedText, package.VersionText);
        Assert.Equal(expectedExactVersion, package.ExactVersion?.ToNormalizedString());
        Assert.Equal("A@" + (expectedExactVersion ?? expectedText), package.Identity);
    }

    [Fact]
    public async Task PackagePath_FileOrFolder()
    {
        await using var temp = TemporaryDirectory.Create();
        Directory.CreateDirectory(temp.FullPath / "packages");
        temp.CreateTextFile("a.nupkg", "");
        temp.CreateTextFile("a.txt", "");

        var (configuration, diagnostics) = Load(temp, """{ "version": 1, "packages": [ { "id": "A", "path": "a.nupkg" }, { "id": "B", "path": "packages" } ] }""");
        Assert.Empty(diagnostics);
        Assert.False(configuration!.Packages[0].IsLocalFolder);
        Assert.Equal(temp.FullPath / "a.nupkg", configuration.Packages[0].LocalPath);
        Assert.True(configuration.Packages[1].IsLocalFolder);
        Assert.Equal(temp.FullPath / "packages", configuration.Packages[1].LocalPath);

        (configuration, diagnostics) = Load(temp, """{ "version": 1, "packages": [ { "id": "A", "path": "a.txt" } ] }""");
        Assert.Null(configuration);
        Assert.Equal("DG1004", Assert.Single(diagnostics).Code);
    }

    [Fact]
    public async Task PackageWithoutProduct_IsAnError()
    {
        await using var temp = TemporaryDirectory.Create();
        var (configuration, diagnostics) = Load(temp, """{ "version": 1, "packages": [ { "id": "A", "version": "1.0.0" }, { "id": "B", "version": "1.0.0" } ], "products": [ { "id": "a", "packages": [ "A" ] } ] }""");

        Assert.Null(configuration);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("DG1012", diagnostic.Code);
        Assert.Equal("$.packages[1]", diagnostic.Location?.JsonPath);
    }

    [Fact]
    public async Task OutputInsideContent_IsAnError()
    {
        await using var temp = TemporaryDirectory.Create();
        temp.CreateTextFile("docs/index.md", "# Home");
        var (configuration, diagnostics) = Load(temp, """{ "version": 1, "content": { "root": "docs" }, "output": "docs/_site" }""");

        Assert.Null(configuration);
        Assert.Contains(diagnostics, d => d.Code == "DG1009");
    }

    [Fact]
    public async Task Credentials_AreNotReportedInDiagnostics()
    {
        await using var temp = TemporaryDirectory.Create();
        var (_, diagnostics) = Load(temp, """{ "version": 1, "packages": [ { "id": "A", "version": "1.0.0", "source": "https://user:secret@example.com/v3/index.json" } ] }""");

        Assert.All(diagnostics, d => Assert.DoesNotContain("secret", d.ToString()));
    }

    [Fact]
    public async Task Loading_DoesNotCreateOutputDirectory()
    {
        await using var temp = TemporaryDirectory.Create();
        var (configuration, _) = Load(temp, """{ "version": 1, "packages": [ { "id": "A", "version": "1.0.0" } ] }""");

        Assert.NotNull(configuration);
        Assert.False(Directory.Exists(configuration.OutputDirectory));
        Assert.False(Directory.Exists(configuration.CacheDirectory) && configuration.CacheDirectory.IsChildOf(temp.FullPath));
    }

    [Fact]
    public async Task DiagnosticOverrides()
    {
        await using var temp = TemporaryDirectory.Create();
        var (configuration, diagnostics) = Load(temp, """{ "version": 1, "packages": [ { "id": "A", "version": "1.0.0" } ], "diagnostics": { "warningsAsErrors": true, "severity": { "DG1010": "info", "dg1001": "warning" } } }""");

        Assert.Empty(diagnostics);
        Assert.NotNull(configuration);
        Assert.Equal(DiagnosticSeverity.Info, configuration.Diagnostics.GetSeverity(DiagnosticDescriptors.RobotsUnderSubpath));
        Assert.Equal(DiagnosticSeverity.Error, configuration.Diagnostics.GetSeverity(DiagnosticDescriptors.UnsupportedConfigurationVersion));
    }

    [Fact]
    public async Task ApiFilter()
    {
        await using var temp = TemporaryDirectory.Create();
        var (configuration, diagnostics) = Load(temp, """{ "version": 1, "packages": [ { "id": "A", "version": "1.0.0" } ], "api": { "include": [ "Contoso.*" ], "exclude": [ "N:Contoso.Internal", "T:Contoso.Hidden*", "M:Contoso.Client.Dispose" ] } }""");

        Assert.Empty(diagnostics);
        Assert.NotNull(configuration);
        var filter = configuration.ApiFilter;
        Assert.True(filter.IsTypeIncluded("Contoso", ["Contoso.Client"]));
        Assert.True(filter.IsTypeIncluded("Contoso.IO", ["Contoso.IO.Reader", "Contoso.IO.Reader.Nested"]));
        Assert.False(filter.IsTypeIncluded("Other", ["Other.Type"]));
        Assert.False(filter.IsTypeIncluded("Contoso.Internal", ["Contoso.Internal.Helper"]));
        Assert.False(filter.IsTypeIncluded("Contoso.Internal.Deep", ["Contoso.Internal.Deep.Helper"]));
        Assert.False(filter.IsTypeIncluded("Contoso", ["Contoso.HiddenType"]));
        Assert.False(filter.IsTypeIncluded("Contoso", ["Contoso.HiddenType", "Contoso.HiddenType.Nested"]));
        Assert.False(filter.IsMemberIncluded('M', "Contoso.Client.Dispose"));
        Assert.True(filter.IsMemberIncluded('P', "Contoso.Client.Dispose"));
    }

    [Fact]
    public async Task Template_IsAValidConfiguration()
    {
        await using var temp = TemporaryDirectory.Create();
        temp.CreateDirectory("docs");
        var (configuration, diagnostics) = Load(temp, ConfigurationTemplate.Create("Contoso.Json", "2.1.0", "Contoso"));

        Assert.Empty(diagnostics);
        Assert.NotNull(configuration);
        Assert.Equal("Contoso", configuration.Site.Title);
    }

    [Fact]
    public async Task Schema_IsUpToDate()
    {
        var path = FullPath.FromPath(RepositoryPaths.Root) / "schema" / "docgen.schema.json";
        var expected = ConfigurationSchema.Generate();
        var actual = File.Exists(path) ? await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken) : null;
        if (actual?.ReplaceLineEndings("\n") != expected && Environment.GetEnvironmentVariable("CI") is null)
        {
            await File.WriteAllTextAsync(path, expected, TestContext.Current.CancellationToken);
            Assert.Fail("The schema was outdated and has been regenerated. Review and commit schema/docgen.schema.json.");
        }

        Assert.Equal(expected, actual?.ReplaceLineEndings("\n"));
    }

    private static (SiteConfiguration? Configuration, IReadOnlyList<Diagnostic> Diagnostics) Load(TemporaryDirectory directory, string json)
    {
        var diagnostics = new DiagnosticBag();
        var path = directory.FullPath / "docgen.json";
        var configuration = ConfigurationLoader.Load(Encoding.UTF8.GetBytes(json), path, diagnostics);
        return (configuration, diagnostics.GetDiagnostics());
    }
}
