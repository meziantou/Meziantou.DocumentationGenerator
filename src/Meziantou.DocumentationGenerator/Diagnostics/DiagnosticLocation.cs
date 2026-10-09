namespace Meziantou.DocumentationGenerator.Diagnostics;

/// <summary>Where a diagnostic applies. Every member is optional; only relevant values are set.</summary>
internal sealed record DiagnosticLocation
{
    /// <summary>File path, relative to the configuration directory when possible, so diagnostics do not leak machine paths into the site.</summary>
    public string? FilePath { get; init; }
    public int? Line { get; init; }
    public int? Column { get; init; }

    /// <summary>JSON path of the configuration value, such as <c>$.packages[0].version</c>.</summary>
    public string? JsonPath { get; init; }
    public string? Package { get; init; }
    public string? TargetFramework { get; init; }
    public string? Assembly { get; init; }
    public string? Symbol { get; init; }

    public override string ToString()
    {
        var parts = new List<string>();
        if (FilePath is not null)
        {
            var file = FilePath;
            if (Line is not null)
            {
                file += Column is not null ? $"({Line},{Column})" : $"({Line})";
            }

            parts.Add(file);
        }

        if (JsonPath is not null)
            parts.Add(JsonPath);

        if (Package is not null)
            parts.Add("package " + Package);

        if (TargetFramework is not null)
            parts.Add("tfm " + TargetFramework);

        if (Assembly is not null)
            parts.Add("assembly " + Assembly);

        if (Symbol is not null)
            parts.Add("symbol " + Symbol);

        return string.Join(", ", parts);
    }
}
