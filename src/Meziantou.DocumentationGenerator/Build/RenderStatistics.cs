using System.Globalization;
using System.Text;
using Meziantou.DocumentationGenerator.Rendering;

namespace Meziantou.DocumentationGenerator.Build;

/// <summary>Collects the time spent on each page, to find what makes the rendering slow.</summary>
internal sealed class RenderStatistics(IReadOnlyList<PageRequest> pages)
{
    private const int SlowestPageCount = 10;

    /// <summary>The timings of the pages, in the order of the pages. Each entry is written by one thread at a time.</summary>
    public PageTiming[] Pages { get; } = new PageTiming[pages.Count];

    public TimeSpan Elapsed { get; set; }

    public int WorkerCount { get; set; }

    public void Report(IBuildReporter reporter)
    {
        if (Pages.Length == 0)
            return;

        var total = Sum(Pages);
        reporter.Info(Format($"  {Pages.Length} pages rendered in {BuildDuration.Format(Elapsed)}; Razor rendering is sequential, the other steps run on {WorkerCount} thread(s)"));
        reporter.Info(Format($"  Cumulative time: Razor rendering {BuildDuration.Format(total.Rendering)}, link extraction {BuildDuration.Format(total.LinkExtraction)}, minification {BuildDuration.Format(total.Minification)}, writing {BuildDuration.Format(total.Writing)}"));
        reporter.Info(Format($"  Output size: {FormatSize(total.RenderedSize)} rendered, {FormatSize(total.MinifiedSize)} minified"));

        var table = new StringBuilder();
        table.AppendLine("  Page kind          Pages   Rendering  Link extraction  Minification   Writing  Avg. size");
        foreach (var group in Enumerable.Range(0, Pages.Length).GroupBy(i => pages[i].Component.Name, StringComparer.Ordinal).OrderByDescending(g => Sum(g.Select(i => Pages[i])).Total))
        {
            var sum = Sum(group.Select(i => Pages[i]));
            var count = group.Count();
            table.AppendLine(Format($"  {group.Key,-16} {count,7} {BuildDuration.Format(sum.Rendering),11} {BuildDuration.Format(sum.LinkExtraction),16} {BuildDuration.Format(sum.Minification),13} {BuildDuration.Format(sum.Writing),9} {FormatSize(sum.RenderedSize / count),10}"));
        }

        table.Append(Format($"  Slowest pages (total time, rendered size):"));
        foreach (var i in Enumerable.Range(0, Pages.Length).OrderByDescending(i => Pages[i].Total).Take(SlowestPageCount))
        {
            table.AppendLine();
            table.Append(Format($"    {BuildDuration.Format(Pages[i].Total),9} {FormatSize(Pages[i].RenderedSize),10}  {pages[i].FilePath}"));
        }

        reporter.Info(table.ToString());
    }

    private static PageTiming Sum(IEnumerable<PageTiming> timings)
    {
        var result = new PageTiming();
        foreach (var timing in timings)
        {
            result.Rendering += timing.Rendering;
            result.LinkExtraction += timing.LinkExtraction;
            result.Minification += timing.Minification;
            result.Writing += timing.Writing;
            result.RenderedSize += timing.RenderedSize;
            result.MinifiedSize += timing.MinifiedSize;
        }

        return result;
    }

    private static string FormatSize(long chars) => chars switch
    {
        < 1024 => Format($"{chars} B"),
        < 1024 * 1024 => Format($"{chars / 1024.0:F1} KB"),
        < 1024 * 1024 * 1024 => Format($"{chars / (1024.0 * 1024):F1} MB"),
        _ => Format($"{chars / (1024.0 * 1024 * 1024):F2} GB"),
    };

    private static string Format(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    internal struct PageTiming
    {
        public TimeSpan Rendering;
        public TimeSpan LinkExtraction;
        public TimeSpan Minification;
        public TimeSpan Writing;

        /// <summary>The number of characters of the rendered and minified markup.</summary>
        public long RenderedSize;
        public long MinifiedSize;

        public readonly TimeSpan Total => Rendering + LinkExtraction + Minification + Writing;
    }
}
