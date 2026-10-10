using System.Text;
using Meziantou.DocumentationGenerator.Configuration;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Packages;
using Meziantou.DocumentationGenerator.Tests.Infrastructure;
using Meziantou.Framework;

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
        var packages = await acquirer.AcquireAsync(configuration.Packages, cancellationToken ?? TestContext.Current.CancellationToken);
        return (packages, diagnostics.GetDiagnostics());
    }
}
