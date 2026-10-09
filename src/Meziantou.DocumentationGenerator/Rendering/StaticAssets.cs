using System.Reflection;

namespace Meziantou.DocumentationGenerator.Rendering;

/// <summary>CSS and JavaScript files embedded in the tool and copied to the <c>_assets</c> directory of the site.</summary>
internal static class StaticAssets
{
    private const string Prefix = "assets/";

    public static IEnumerable<(string FileName, byte[] Content)> GetAll()
    {
        var assembly = typeof(StaticAssets).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            yield return (name[Prefix.Length..].Replace('\\', '/'), memory.ToArray());
        }
    }
}
