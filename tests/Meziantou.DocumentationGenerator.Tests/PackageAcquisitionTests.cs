using System.Text;
using Meziantou.DocumentationGenerator.Configuration;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Packages;
using Meziantou.DocumentationGenerator.Tests.Infrastructure;
using Meziantou.Framework;
using NuGet.Versioning;

namespace Meziantou.DocumentationGenerator.Tests;

public sealed class PackageAcquisitionTests
{
    [Fact]
    public async Task LocalPackage_IsUsedInPlace()
    {
        await using var temp = TemporaryDirectory.Create();
        var path = new TestPackageBuilder("Contoso.Json", "2.1.0")
            .WithNuspecMetadata("""<repository type="git" url="https://github.com/contoso/json" commit="0123456789abcdef0123456789abcdef01234567" /><readme>docs/README.md</readme>""")
            .AddFile("docs/README.md", "# Readme")
            .Save(temp.FullPath / "packages");
        var (packages, diagnostics) = await AcquireAsync(temp, $$"""{ "version": 1, "packages": [ { "id": "Contoso.Json", "version": "2.1.0", "path": "packages/{{path.Name}}" } ] }""");

        Assert.Empty(diagnostics);
        var package = Assert.Single(packages);
        Assert.Equal(path, package.Path);
        Assert.Equal("local", package.Source);
        Assert.Equal("https://github.com/contoso/json", package.Metadata.RepositoryUrl);
        Assert.Equal("0123456789abcdef0123456789abcdef01234567", package.Metadata.RepositoryCommit);
        Assert.Equal("docs/README.md", package.Metadata.ReadmePath);
        Assert.Equal(PackageAcquirer.ComputeSha512(path), package.Sha512);
    }

    [Fact]
    public async Task LocalPackage_IdentityMismatch()
    {
        await using var temp = TemporaryDirectory.Create();
        var path = new TestPackageBuilder("Contoso.Json", "2.0.0").Save(temp.FullPath, "contoso.json.nupkg");
        var (packages, diagnostics) = await AcquireAsync(temp, """{ "version": 1, "packages": [ { "id": "Contoso.Json", "version": "2.1.0", "path": "contoso.json.nupkg" } ] }""");

        Assert.Empty(packages);
        Assert.Equal("DG2009", Assert.Single(diagnostics).Code);
    }

    [Fact]
    public async Task CorruptLocalPackage()
    {
        await using var temp = TemporaryDirectory.Create();
        temp.CreateTextFile("bad.nupkg", "not a zip");
        var (packages, diagnostics) = await AcquireAsync(temp, """{ "version": 1, "packages": [ { "id": "Bad", "version": "1.0.0", "path": "bad.nupkg" } ] }""");

        Assert.Empty(packages);
        Assert.Equal("DG2003", Assert.Single(diagnostics).Code);
    }

    [Fact]
    public async Task FolderFeed_DownloadsIntoCacheAndReusesIt()
    {
        await using var temp = TemporaryDirectory.Create();
        new TestPackageBuilder("Contoso.Json", "2.1.0").AddFile("lib/net8.0/Contoso.Json.dll", "fake").Save(temp.FullPath / "feed");
        WriteNuGetConfig(temp, ("feed", temp.FullPath / "feed"));
        const string Configuration = """{ "version": 1, "cacheDirectory": "cache", "packages": [ { "id": "Contoso.Json", "version": "2.1.0" } ] }""";

        var (first, diagnostics) = await AcquireAsync(temp, Configuration);
        Assert.Empty(diagnostics);
        var package = Assert.Single(first);
        Assert.True(package.Path.IsChildOf(temp.FullPath / "cache"));
        Assert.Equal("feed", package.Source);

        // The feed is no longer available: the cache is used and the package is unchanged
        Directory.Delete(temp.FullPath / "feed", recursive: true);
        var (second, diagnostics2) = await AcquireAsync(temp, Configuration);
        Assert.Empty(diagnostics2);
        Assert.Equal(package.Sha512, Assert.Single(second).Sha512);
        Assert.Equal(package.Path, second[0].Path);
    }

    [Theory]
    [InlineData("*", "2.1.0")]
    [InlineData("*-*", "3.0.0-preview.1")]
    [InlineData("1.*", "1.5.0")]
    [InlineData("2.*-*", "2.1.0")]
    [InlineData("[1.0,2.0)", "1.5.0")]
    [InlineData("[1.0,3.0)", "2.1.0")]
    [InlineData("[2.0.0-beta.1,3.0)", "3.0.0-preview.1")] // Like NuGet, 3.0.0-preview.1 is lower than 3.0.0
    [InlineData("[2.0.0-beta.1,2.0.0)", "2.0.0-beta.1")]
    [InlineData("[1.0,)", "2.1.0")]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("[4.0,)", null)]
    public void SelectVersion_PicksTheHighestMatchingVersion(string range, string? expected)
    {
        var versions = new[] { "1.0.0", "1.5.0", "2.0.0-beta.1", "2.0.0", "2.1.0", "3.0.0-preview.1" }.Select(NuGetVersion.Parse).ToList();
        Assert.True(ConfigurationLoader.TryParseVersionRange(range, out var versionRange));

        Assert.Equal(expected, PackageAcquirer.SelectVersion(versionRange, versions)?.ToNormalizedString());
    }

    [Fact]
    public void SelectVersion_StableOnlyRangeIgnoresPrereleases()
    {
        Assert.True(ConfigurationLoader.TryParseVersionRange("*", out var range));
        Assert.Null(PackageAcquirer.SelectVersion(range, [NuGetVersion.Parse("1.0.0-alpha"), NuGetVersion.Parse("1.0.0-beta")]));
    }

    [Theory]
    [InlineData("", "2.1.0")]
    [InlineData(""", "version": "*" """, "2.1.0")]
    [InlineData(""", "version": "*-*" """, "3.0.0-preview.1")]
    [InlineData(""", "version": "[1.0,2.0)" """, "1.5.0")]
    public async Task FolderFeed_ResolvesVersionRange(string versionProperty, string expectedVersion)
    {
        await using var temp = TemporaryDirectory.Create();
        foreach (var version in new[] { "1.0.0", "1.5.0", "2.1.0", "3.0.0-preview.1" })
        {
            new TestPackageBuilder("Contoso.Json", version).Save(temp.FullPath / "feed");
        }

        WriteNuGetConfig(temp, ("feed", temp.FullPath / "feed"));
        var (packages, diagnostics) = await AcquireAsync(temp, $$"""{ "version": 1, "cacheDirectory": "cache", "packages": [ { "id": "Contoso.Json"{{versionProperty}} } ] }""");

        var package = Assert.Single(packages);
        Assert.Equal(expectedVersion, package.Metadata.Version.ToNormalizedString());
        Assert.Equal("Contoso.Json@" + expectedVersion, package.Identity);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("DG2015", diagnostic.Code);
        Assert.Contains("resolves to " + expectedVersion, diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FolderFeed_HighestVersionAcrossSources()
    {
        await using var temp = TemporaryDirectory.Create();
        new TestPackageBuilder("Contoso.Json", "1.0.0").Save(temp.FullPath / "a");
        new TestPackageBuilder("Contoso.Json", "2.0.0").Save(temp.FullPath / "b");
        WriteNuGetConfig(temp, ("a", temp.FullPath / "a"), ("b", temp.FullPath / "b"));

        var (packages, _) = await AcquireAsync(temp, """{ "version": 1, "cacheDirectory": "cache", "packages": [ { "id": "Contoso.Json" } ] }""");

        var package = Assert.Single(packages);
        Assert.Equal("2.0.0", package.Metadata.Version.ToNormalizedString());
        Assert.Equal("b", package.Source);
    }

    [Fact]
    public async Task FolderFeed_NoMatchingVersion()
    {
        await using var temp = TemporaryDirectory.Create();
        new TestPackageBuilder("Contoso.Json", "1.0.0-beta").Save(temp.FullPath / "feed");
        WriteNuGetConfig(temp, ("feed", temp.FullPath / "feed"));

        var (packages, diagnostics) = await AcquireAsync(temp, """{ "version": 1, "cacheDirectory": "cache", "packages": [ { "id": "Contoso.Json" } ] }""");

        Assert.Empty(packages);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("DG2001", diagnostic.Code);
        Assert.Contains("No version of Contoso.Json matching '*'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("*-*", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Offline_VersionRangeResolvesFromCache()
    {
        await using var temp = TemporaryDirectory.Create();
        new TestPackageBuilder("Contoso.Json", "1.0.0").Save(temp.FullPath / "feed");
        new TestPackageBuilder("Contoso.Json", "1.5.0").Save(temp.FullPath / "feed");
        WriteNuGetConfig(temp, ("feed", temp.FullPath / "feed"));
        await AcquireAsync(temp, """{ "version": 1, "cacheDirectory": "cache", "packages": [ { "id": "Contoso.Json", "version": "1.0.0" } ] }""");
        await AcquireAsync(temp, """{ "version": 1, "cacheDirectory": "cache", "packages": [ { "id": "Contoso.Json", "version": "1.5.0" } ] }""");

        // A newer version on the feed is ignored offline
        new TestPackageBuilder("Contoso.Json", "2.0.0").Save(temp.FullPath / "feed");
        var (packages, diagnostics) = await AcquireAsync(temp, """{ "version": 1, "cacheDirectory": "cache", "packages": [ { "id": "Contoso.Json", "version": "*" } ] }""", offline: true);
        Assert.Equal("1.5.0", Assert.Single(packages).Metadata.Version.ToNormalizedString());
        Assert.Equal("DG2015", Assert.Single(diagnostics).Code);

        (packages, diagnostics) = await AcquireAsync(temp, """{ "version": 1, "cacheDirectory": "cache", "packages": [ { "id": "Contoso.Json", "version": "2.*" } ] }""", offline: true);
        Assert.Empty(packages);
        Assert.Equal("DG2006", Assert.Single(diagnostics).Code);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData(""", "version": "2.*" """, true)]
    [InlineData(""", "version": "[1.0,2.0)" """, false)]
    public async Task LocalPackage_VersionRange(string versionProperty, bool expectedMatch)
    {
        await using var temp = TemporaryDirectory.Create();
        var path = new TestPackageBuilder("Contoso.Json", "2.1.0").Save(temp.FullPath, "contoso.json.nupkg");
        var (packages, diagnostics) = await AcquireAsync(temp, $$"""{ "version": 1, "packages": [ { "id": "Contoso.Json"{{versionProperty}}, "path": "contoso.json.nupkg" } ] }""");

        if (expectedMatch)
        {
            Assert.Empty(diagnostics);
            Assert.Equal(path, Assert.Single(packages).Path);
        }
        else
        {
            Assert.Empty(packages);
            Assert.Equal("DG2009", Assert.Single(diagnostics).Code);
        }
    }

    [Theory]
    [InlineData("", "2.1.0")]
    [InlineData(""", "version": "*-*" """, "3.0.0-preview.1")]
    [InlineData(""", "version": "[1.0,2.0)" """, "1.5.0")]
    [InlineData(""", "version": "1.0.0" """, "1.0.0")]
    public async Task LocalFolder_UsesHighestMatchingPackage(string versionProperty, string expectedVersion)
    {
        await using var temp = TemporaryDirectory.Create();
        foreach (var version in new[] { "1.0.0", "1.5.0", "2.1.0", "3.0.0-preview.1" })
        {
            new TestPackageBuilder("Contoso.Json", version).Save(temp.FullPath / "packages");
        }

        // Other packages of the folder are ignored, even when their identifier starts with the same prefix
        new TestPackageBuilder("Contoso.Json.Extensions", "9.0.0").Save(temp.FullPath / "packages");
        new TestPackageBuilder("Other", "9.0.0").Save(temp.FullPath / "packages");

        var (packages, diagnostics) = await AcquireAsync(temp, $$"""{ "version": 1, "packages": [ { "id": "Contoso.Json"{{versionProperty}}, "path": "packages" } ] }""", offline: true);

        var package = Assert.Single(packages);
        Assert.Equal(expectedVersion, package.Metadata.Version.ToNormalizedString());
        Assert.Equal("local", package.Source);
        Assert.Equal(temp.FullPath / "packages", package.Path.Parent);
        Assert.All(diagnostics, d => Assert.Equal("DG2015", d.Code));
    }

    [Fact]
    public async Task LocalFolder_DiscoversAllPackageIdsAndPreservesOptions()
    {
        await using var temp = TemporaryDirectory.Create();
        new TestPackageBuilder("Contoso.Json", "1.0.0").Save(temp.FullPath / "packages");
        new TestPackageBuilder("contoso.json", "2.0.0").Save(temp.FullPath / "packages", "renamed.NUPKG");
        var latest = new TestPackageBuilder("Contoso.Json", "3.0.0-preview").Save(temp.FullPath / "packages");
        var other = new TestPackageBuilder("Other", "1.0.0").Save(temp.FullPath / "packages", "arbitrary.nupkg");
        new TestPackageBuilder("Nested", "1.0.0").Save(temp.FullPath / "packages" / "nested");
        temp.CreateTextFile("packages/ignored.symbols.nupkg", "not a zip");
        temp.CreateTextFile("packages/ignored.snupkg", "not a zip");
        temp.CreateTextFile("packages/README.txt", "not a package");

        var (packages, diagnostics) = await AcquireAsync(temp, """
            {
              "version": 1,
              "packages": [
                { "path": "packages", "targetFrameworks": ["net8.0"], "assemblies": ["Library.dll"], "defineConstants": ["FEATURE"] }
              ]
            }
            """, offline: true);

        Assert.Equal(["Contoso.Json@3.0.0-preview", "Other@1.0.0"], packages.Select(p => p.Identity));
        Assert.Equal([latest, other], packages.Select(p => p.Path));
        Assert.All(packages, package =>
        {
            Assert.Equal("local", package.Source);
            Assert.NotNull(package.Configuration.Id);
            Assert.Equal(["net8.0"], package.Configuration.TargetFrameworks.Select(f => f.GetShortFolderName()));
            Assert.Equal(["Library.dll"], package.Configuration.Assemblies);
            Assert.Equal(["FEATURE"], package.Configuration.DefineConstants);
            Assert.Equal("$.packages[0]", package.Configuration.JsonPath);
        });
        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, d => Assert.Equal("DG2015", d.Code));
    }

    [Theory]
    [InlineData("*", "2.0.0")]
    [InlineData("*-*", "3.0.0-preview")]
    [InlineData("[1.0,2.0)", "1.0.0")]
    [InlineData("2.0.0", "2.0.0")]
    public async Task LocalFolder_DiscoveryUsesVersionFilterForEachId(string version, string expected)
    {
        await using var temp = TemporaryDirectory.Create();
        foreach (var id in new[] { "A", "B" })
        {
            foreach (var candidate in new[] { "1.0.0", "2.0.0", "3.0.0-preview" })
            {
                new TestPackageBuilder(id, candidate).Save(temp.FullPath / "packages");
            }
        }

        var (packages, diagnostics) = await AcquireAsync(temp, $$"""{ "version": 1, "packages": [ { "path": "packages", "version": "{{version}}" } ] }""", offline: true);

        Assert.Equal(["A", "B"], packages.Select(p => p.Id));
        Assert.All(packages, p => Assert.Equal(expected, p.Metadata.Version.ToNormalizedString()));
        Assert.DoesNotContain(diagnostics, d => d.Severity >= DiagnosticSeverity.Warning);
    }

    [Theory]
    [InlineData("packages")]
    [InlineData("references")]
    public async Task LocalFolder_DiscoveryIncludesPrereleaseOnlyPackagesByDefault(string section)
    {
        await using var temp = TemporaryDirectory.Create();
        new TestPackageBuilder("PrereleaseOnly", "1.0.0-preview.1").Save(temp.FullPath / "packages");
        temp.CreateTextFile("docs/index.md", "# Home");

        var (packages, diagnostics) = await AcquireAsync(temp, $$"""
            {"version":1,"content":{"root":"docs"},"{{section}}":[{"path":"packages"}]}
            """, offline: true);

        var package = Assert.Single(packages);
        Assert.Equal("PrereleaseOnly", package.Id);
        Assert.Equal("1.0.0-preview.1", package.Metadata.Version.ToNormalizedString());
        Assert.Equal("*-*", package.Configuration.VersionText);
        Assert.Equal(section == "references", package.Configuration.IsReference);
        Assert.DoesNotContain(diagnostics, d => d.Severity >= DiagnosticSeverity.Warning);
    }

    [Theory]
    [InlineData("empty", "DG2001")]
    [InlineData("unmatched", "DG2001")]
    [InlineData("corrupt", "DG2003")]
    [InlineData("invalid-id", "DG2003")]
    [InlineData("unsafe", "DG2005")]
    public async Task LocalFolder_DiscoveryReportsErrors(string scenario, string expectedCode)
    {
        await using var temp = TemporaryDirectory.Create();
        Directory.CreateDirectory(temp.FullPath / "packages");
        if (scenario == "unmatched")
            new TestPackageBuilder("A", "0.9.0-preview").Save(temp.FullPath / "packages");
        if (scenario == "corrupt")
            temp.CreateTextFile("packages/bad.nupkg", "not a zip");
        if (scenario == "invalid-id")
            new TestPackageBuilder("Bad Id", "1.0.0").Save(temp.FullPath / "packages");
        if (scenario == "unsafe")
            new TestPackageBuilder("A", "1.0.0").AddFile("../escape.txt", "unsafe").Save(temp.FullPath / "packages");

        var (packages, diagnostics) = await AcquireAsync(temp, """{ "version": 1, "packages": [ { "path": "packages", "version": "1.*-*" } ] }""");

        Assert.Empty(packages);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(expectedCode, diagnostic.Code);
        Assert.Equal("$.packages[0]", diagnostic.Location?.JsonPath);
    }

    [Theory]
    [InlineData("""{ "id": "a", "path": "packages/a.1.0.0.nupkg" }, { "path": "packages" }""", "")]
    [InlineData("""{ "path": "packages" }, { "id": "a", "path": "packages/a.1.0.0.nupkg" }""", "")]
    [InlineData("""{ "path": "packages" }, { "path": "packages" }""", "")]
    [InlineData("""{ "path": "packages" }""", """, "references": [ { "path": "packages" } ]""")]
    public async Task LocalFolder_DiscoveryRejectsDuplicateIds(string entries, string references)
    {
        await using var temp = TemporaryDirectory.Create();
        new TestPackageBuilder("A", "1.0.0").Save(temp.FullPath / "packages");

        var (packages, diagnostics) = await AcquireAsync(temp, $$"""{ "version": 1, "packages": [ {{entries}} ]{{references}} }""");

        Assert.Single(packages);
        var diagnostic = Assert.Single(diagnostics, d => d.Code == "DG1008");
        Assert.Contains("'A'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocalFolder_NoMatchingPackage()
    {
        await using var temp = TemporaryDirectory.Create();
        new TestPackageBuilder("Contoso.Json", "1.0.0-ci.42").Save(temp.FullPath / "packages");

        var (packages, diagnostics) = await AcquireAsync(temp, """{ "version": 1, "packages": [ { "id": "Contoso.Json", "path": "packages" } ] }""");
        Assert.Empty(packages);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("DG2001", diagnostic.Code);
        Assert.Contains("*-*", diagnostic.Message, StringComparison.Ordinal);

        (packages, diagnostics) = await AcquireAsync(temp, """{ "version": 1, "packages": [ { "id": "Contoso.Json", "version": "1.0.0", "path": "packages" } ] }""");
        Assert.Empty(packages);
        Assert.Equal("DG2001", Assert.Single(diagnostics).Code);
    }

    [Fact]
    public async Task LocalFolder_CorruptPackage()
    {
        await using var temp = TemporaryDirectory.Create();
        new TestPackageBuilder("Contoso.Json", "1.0.0").Save(temp.FullPath / "packages");
        temp.CreateTextFile("packages/Contoso.Json.2.0.0.nupkg", "not a zip");

        var (packages, diagnostics) = await AcquireAsync(temp, """{ "version": 1, "packages": [ { "id": "Contoso.Json", "path": "packages" } ] }""");
        Assert.Empty(packages);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("DG2003", diagnostic.Code);
        Assert.Contains("Contoso.Json.2.0.0.nupkg", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SourceSelection_ByName()
    {
        await using var temp = TemporaryDirectory.Create();
        new TestPackageBuilder("Contoso.Json", "2.1.0").Save(temp.FullPath / "b");
        Directory.CreateDirectory(temp.FullPath / "a");
        WriteNuGetConfig(temp, ("a", temp.FullPath / "a"), ("b", temp.FullPath / "b"));

        var (packages, diagnostics) = await AcquireAsync(temp, """{ "version": 1, "cacheDirectory": "cache", "packages": [ { "id": "Contoso.Json", "version": "2.1.0", "source": "b" } ] }""");
        Assert.Empty(diagnostics);
        Assert.Equal("b", Assert.Single(packages).Source);

        (packages, diagnostics) = await AcquireAsync(temp, """{ "version": 1, "cacheDirectory": "cache2", "packages": [ { "id": "Contoso.Json", "version": "2.1.0", "source": "a" } ] }""");
        Assert.Empty(packages);
        Assert.Equal("DG2001", Assert.Single(diagnostics).Code);

        (packages, diagnostics) = await AcquireAsync(temp, """{ "version": 1, "cacheDirectory": "cache3", "packages": [ { "id": "Contoso.Json", "version": "2.1.0", "source": "unknown" } ] }""");
        Assert.Empty(packages);
        Assert.Contains("not defined in NuGet.Config", Assert.Single(diagnostics).Message);
    }

    [Fact]
    public async Task SourceMapping_IsHonored()
    {
        await using var temp = TemporaryDirectory.Create();
        new TestPackageBuilder("Contoso.Json", "2.1.0").Save(temp.FullPath / "a");
        new TestPackageBuilder("Contoso.Json", "2.1.0").Save(temp.FullPath / "b");
        WriteNuGetConfig(temp, [("a", temp.FullPath / "a"), ("b", temp.FullPath / "b")], mapping: """
            <packageSourceMapping>
              <packageSource key="b"><package pattern="Contoso.*" /></packageSource>
              <packageSource key="a"><package pattern="Other.*" /></packageSource>
            </packageSourceMapping>
            """);

        var (packages, diagnostics) = await AcquireAsync(temp, """{ "version": 1, "cacheDirectory": "cache", "packages": [ { "id": "Contoso.Json", "version": "2.1.0" } ] }""");
        Assert.Empty(diagnostics);
        Assert.Equal("b", Assert.Single(packages).Source);
    }

    [Fact]
    public async Task UnavailablePackage_ExactVersionIsRequired()
    {
        await using var temp = TemporaryDirectory.Create();
        new TestPackageBuilder("Contoso.Json", "2.0.0").Save(temp.FullPath / "feed");
        WriteNuGetConfig(temp, ("feed", temp.FullPath / "feed"));

        var (packages, diagnostics) = await AcquireAsync(temp, """{ "version": 1, "cacheDirectory": "cache", "packages": [ { "id": "Contoso.Json", "version": "2.1.0" } ] }""");
        Assert.Empty(packages);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("DG2001", diagnostic.Code);
        Assert.Contains("Contoso.Json@2.1.0", diagnostic.Message);
    }

    [Fact]
    public async Task HttpFeed_WithCredentialsFromNuGetConfig()
    {
        await using var temp = TemporaryDirectory.Create();
        await using var feed = await TestNuGetFeed.StartAsync("user", "s3cr3t-password");
        feed.Add(new TestPackageBuilder("Contoso.Json", "2.1.0"));
        WriteNuGetConfig(temp, [("feed", feed.ServiceIndexUrl)], credentials: """
            <packageSourceCredentials>
              <feed>
                <add key="Username" value="user" />
                <add key="ClearTextPassword" value="s3cr3t-password" />
              </feed>
            </packageSourceCredentials>
            """);

        var (packages, diagnostics) = await AcquireAsync(temp, """{ "version": 1, "cacheDirectory": "cache", "packages": [ { "id": "Contoso.Json", "version": "2.1.0" } ] }""");
        Assert.Empty(diagnostics);
        Assert.Equal("feed", Assert.Single(packages).Source);
    }

    [Fact]
    public async Task HttpFeed_AuthenticationFailure()
    {
        await using var temp = TemporaryDirectory.Create();
        await using var feed = await TestNuGetFeed.StartAsync("user", "s3cr3t-password");
        feed.Add(new TestPackageBuilder("Contoso.Json", "2.1.0"));
        WriteNuGetConfig(temp, [("feed", feed.ServiceIndexUrl)], credentials: """
            <packageSourceCredentials>
              <feed>
                <add key="Username" value="user" />
                <add key="ClearTextPassword" value="wrong-password" />
              </feed>
            </packageSourceCredentials>
            """);

        var (packages, diagnostics) = await AcquireAsync(temp, """{ "version": 1, "cacheDirectory": "cache", "packages": [ { "id": "Contoso.Json", "version": "2.1.0" } ] }""");
        Assert.Empty(packages);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("DG2002", diagnostic.Code);
        Assert.Contains("authentication failed", diagnostic.Message);
        Assert.DoesNotContain("password", diagnostic.ToString(), ignoreCase: true);
    }

    [Fact]
    public async Task Offline_CacheHitAndMiss()
    {
        await using var temp = TemporaryDirectory.Create();
        new TestPackageBuilder("Contoso.Json", "2.1.0").Save(temp.FullPath / "feed");
        WriteNuGetConfig(temp, ("feed", temp.FullPath / "feed"));
        const string Configuration = """{ "version": 1, "cacheDirectory": "cache", "packages": [ { "id": "Contoso.Json", "version": "2.1.0" } ] }""";

        var (packages, diagnostics) = await AcquireAsync(temp, Configuration, offline: true);
        Assert.Empty(packages);
        Assert.Equal("DG2006", Assert.Single(diagnostics).Code);

        (packages, diagnostics) = await AcquireAsync(temp, Configuration);
        Assert.Single(packages);

        Directory.Delete(temp.FullPath / "feed", recursive: true);
        (packages, diagnostics) = await AcquireAsync(temp, Configuration, offline: true);
        Assert.Empty(diagnostics);
        Assert.Single(packages);
    }

    [Fact]
    public async Task CorruptCacheEntry_IsDownloadedAgain()
    {
        await using var temp = TemporaryDirectory.Create();
        new TestPackageBuilder("Contoso.Json", "2.1.0").Save(temp.FullPath / "feed");
        WriteNuGetConfig(temp, ("feed", temp.FullPath / "feed"));
        const string Configuration = """{ "version": 1, "cacheDirectory": "cache", "packages": [ { "id": "Contoso.Json", "version": "2.1.0" } ] }""";

        var (packages, _) = await AcquireAsync(temp, Configuration);
        var package = Assert.Single(packages);
        await File.WriteAllTextAsync(package.Path, "truncated", TestContext.Current.CancellationToken);

        // Offline: the corrupt entry cannot be replaced
        var (offlinePackages, offlineDiagnostics) = await AcquireAsync(temp, Configuration, offline: true);
        Assert.Empty(offlinePackages);
        Assert.Equal("DG2003", Assert.Single(offlineDiagnostics).Code);

        var (repaired, diagnostics) = await AcquireAsync(temp, Configuration);
        Assert.Empty(diagnostics);
        Assert.Equal(package.Sha512, Assert.Single(repaired).Sha512);
    }

    [Fact]
    public async Task UnsafeEntries_AreRejected()
    {
        await using var temp = TemporaryDirectory.Create();
        new TestPackageBuilder("Evil", "1.0.0")
            .AddFile("../evil.txt", "evil")
            .AddFile("/rooted.txt", "evil")
            .AddFile("C:/windows.txt", "evil")
            .AddFile("lib/net8.0/%2E%2E/escaped.txt", "evil")
            .AddFile("lib/a.txt", "a")
            .AddFile("LIB/A.txt", "duplicate")
            .AddSymbolicLink("lib/link", "/etc/passwd")
            .Save(temp.FullPath / "packages");
        var (packages, diagnostics) = await AcquireAsync(temp, """{ "version": 1, "packages": [ { "id": "Evil", "version": "1.0.0", "path": "packages/evil.1.0.0.nupkg" } ] }""");

        Assert.Empty(packages);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("DG2005", diagnostic.Code);
        Assert.Contains("'../evil.txt'", diagnostic.Message);
        Assert.Contains("'/rooted.txt'", diagnostic.Message);
        Assert.Contains("'C:/windows.txt'", diagnostic.Message);
        Assert.Contains("'lib/net8.0/%2E%2E/escaped.txt'", diagnostic.Message);
        Assert.Contains("'LIB/A.txt': duplicate entry", diagnostic.Message);
        Assert.Contains("'lib/link': symbolic links are not allowed", diagnostic.Message);
        Assert.False(File.Exists(temp.FullPath / "evil.txt"));
        Assert.False(File.Exists(temp.FullPath.Parent / "evil.txt"));
    }

    [Fact]
    public async Task NativeOnlyPackage_IsAcquired()
    {
        await using var temp = TemporaryDirectory.Create();
        new TestPackageBuilder("Native", "1.0.0").AddFile("runtimes/linux-x64/native/libnative.so", "elf").Save(temp.FullPath / "packages");
        var (packages, diagnostics) = await AcquireAsync(temp, """{ "version": 1, "packages": [ { "id": "Native", "version": "1.0.0", "path": "packages/native.1.0.0.nupkg" } ] }""");

        Assert.Empty(diagnostics);
        Assert.Single(packages);
    }

    [Fact]
    public async Task Cancellation()
    {
        await using var temp = TemporaryDirectory.Create();
        new TestPackageBuilder("Contoso.Json", "2.1.0").Save(temp.FullPath / "feed");
        WriteNuGetConfig(temp, ("feed", temp.FullPath / "feed"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AcquireAsync(temp, """{ "version": 1, "cacheDirectory": "cache", "packages": [ { "id": "Contoso.Json", "version": "2.1.0" } ] }""", cancellationToken: cts.Token));
        Assert.False(Directory.Exists(temp.FullPath / "cache" / "packages" / "contoso.json" / "2.1.0") && Directory.EnumerateFiles(temp.FullPath / "cache" / "packages" / "contoso.json" / "2.1.0", "*.nupkg").Any());
    }

    [Fact]
    public void ArchiveEntryNames()
    {
        Assert.Equal("lib/net8.0/A+B.dll", PackageArchive.NormalizeEntryName("lib/net8.0/A%2BB.dll", out _));
        Assert.Equal("lib/net8.0/A.dll", PackageArchive.NormalizeEntryName("lib\\net8.0\\A.dll", out _));
        Assert.Null(PackageArchive.NormalizeEntryName("a/../../b", out _));
        Assert.Null(PackageArchive.NormalizeEntryName("\\\\server\\share", out _));
    }

    internal static void WriteNuGetConfig(TemporaryDirectory directory, params (string Name, string Url)[] sources) => WriteNuGetConfig(directory, sources, mapping: null, credentials: null);

    internal static void WriteNuGetConfig(TemporaryDirectory directory, (string Name, string Url)[] sources, string? mapping = null, string? credentials = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        builder.AppendLine("<configuration>");
        builder.AppendLine("  <packageSources>");
        builder.AppendLine("    <clear />");
        foreach (var (name, url) in sources)
        {
            builder.AppendLine($"    <add key=\"{name}\" value=\"{url}\" allowInsecureConnections=\"true\" />");
        }

        builder.AppendLine("  </packageSources>");
        builder.AppendLine(mapping);
        builder.AppendLine(credentials);
        builder.AppendLine("</configuration>");
        directory.CreateTextFile("NuGet.Config", builder.ToString());
    }

    private static async Task<(IReadOnlyList<AcquiredPackage> Packages, IReadOnlyList<Diagnostic> Diagnostics)> AcquireAsync(TemporaryDirectory directory, string json, bool offline = false, CancellationToken? cancellationToken = null)
    {
        var diagnostics = new DiagnosticBag();
        var configuration = ConfigurationLoader.Load(Encoding.UTF8.GetBytes(json), directory.FullPath / "docgen.json", diagnostics);
        Assert.NotNull(configuration);
        Assert.Empty(diagnostics.GetDiagnostics());

        var acquirer = new PackageAcquirer(configuration.CacheDirectory, configuration.BaseDirectory, offline, diagnostics);
        var packages = await acquirer.AcquireAsync([.. configuration.Packages, .. configuration.References], cancellationToken ?? TestContext.Current.CancellationToken);
        return (packages, diagnostics.GetDiagnostics());
    }
}
