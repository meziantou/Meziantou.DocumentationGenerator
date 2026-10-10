namespace Meziantou.DocumentationGenerator.Configuration;

internal enum RobotsPolicy
{
    /// <summary>No robots.txt and no robots meta tag. The host decides.</summary>
    None,

    /// <summary>Generate a robots.txt allowing crawlers and referencing the sitemap.</summary>
    Allow,

    /// <summary>Generate a robots.txt disallowing crawlers and add a noindex meta tag to every page.</summary>
    Disallow,
}
