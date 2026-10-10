using System.Diagnostics;
using System.Text;
using Meziantou.DocumentationGenerator.Cli;
using Meziantou.DocumentationGenerator.Tests.Infrastructure;

namespace Meziantou.DocumentationGenerator.Tests.EndToEnd;

/// <summary>
/// Measures a large synthetic API (documented budgets in docs/performance.md): 1,000 types with 12 members each,
/// for 2 target frameworks.
/// </summary>
public sealed class PerformanceTests(ITestOutputHelper output)
{
    private const int TypeCount = 1_000;
    private static readonly TimeSpan BuildTimeBudget = TimeSpan.FromMinutes(3);
    private const long AverageTypePageBudget = 120 * 1024;
    private const long SearchIndexBudget = 3 * 1024 * 1024;

    [Fact]
    public async Task LargeCorpus_StaysWithinBudgets()
    {
        var source = new StringBuilder();
        for (var i = 0; i < TypeCount; i++)
        {
            source.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $$"""
                namespace Contoso.Large.Namespace{{i % 20}}
                {
                    /// <summary>Type {{i}} with a <see cref="Contoso.Large.Namespace0.Type0"/> reference.</summary>
                    public class Type{{i}}
                    {
                        /// <summary>Property.</summary>
                        public int Value { get; set; }
                        /// <summary>Name.</summary>
                        public string? Name { get; set; }
                        /// <summary>Event.</summary>
                        public event System.EventHandler? Changed;
                        /// <summary>Constant.</summary>
                        public const int Constant = {{i}};
                        /// <summary>Constructor.</summary>
                        public Type{{i}}() { }
                        /// <summary>Constructor.</summary>
                        /// <param name="value">The value.</param>
                        public Type{{i}}(int value) { Value = value; }
                        /// <summary>Method.</summary>
                        /// <param name="value">The value.</param>
                        /// <returns>The result.</returns>
                        public int Compute(int value) => value;
                        /// <summary>Overload.</summary>
                        public int Compute(string value) => 0;
                        /// <summary>Async method.</summary>
                        public System.Threading.Tasks.Task RunAsync() => System.Threading.Tasks.Task.CompletedTask;
                        /// <summary>Generic method.</summary>
                        public T Get<T>() where T : new() => new T();
                        /// <summary>Operator.</summary>
                        public static Type{{i}} operator +(Type{{i}} a, Type{{i}} b) => a;
                        /// <summary>Changes.</summary>
                        protected virtual void OnChanged() => Changed?.Invoke(this, System.EventArgs.Empty);
                    }
                }
                """);
        }

        var assembly = TestCompiler.Compile("Contoso.Large", source.ToString());
        var package = new TestPackageBuilder("Contoso.Large", "1.0.0")
            .AddFile("README.md", "# Large")
            .AddFile("lib/net8.0/Contoso.Large.dll", assembly.Assembly).AddFile("lib/net8.0/Contoso.Large.xml", assembly.Xml)
            .AddFile("lib/netstandard2.0/Contoso.Large.dll", assembly.Assembly).AddFile("lib/netstandard2.0/Contoso.Large.xml", assembly.Xml);

        var stopwatch = Stopwatch.StartNew();
        var allocatedBefore = GC.GetTotalAllocatedBytes();
        await using var site = await SiteBuilder.BuildAsync([package]);
        stopwatch.Stop();
        var allocated = GC.GetTotalAllocatedBytes() - allocatedBefore;
        Assert.Equal(ExitCodes.Success, site.Result.ExitCode, site.Result.AllOutput);

        var typePages = Directory.GetFiles(site.Output / "contoso.large" / "api", "Contoso.Large.*.html");
        var averagePageSize = (long)typePages.Average(f => new FileInfo(f).Length);
        var indexSize = new FileInfo(site.Output / "_assets" / "search-index.json").Length;
        var totalSize = Directory.EnumerateFiles(site.Output, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        output.WriteLine($"Build time: {stopwatch.Elapsed}; allocated: {allocated / 1024 / 1024} MB; pages: {typePages.Length}; average type page: {averagePageSize / 1024} KB; search index: {indexSize / 1024} KB; site: {totalSize / 1024 / 1024} MB");

        Assert.HasCount(TypeCount, typePages);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.Zero, BuildTimeBudget);
        Assert.InRange(averagePageSize, 0, AverageTypePageBudget);
        Assert.InRange(indexSize, 0, SearchIndexBudget);
    }
}
