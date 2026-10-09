using Microsoft.AspNetCore.Components;

namespace Meziantou.DocumentationGenerator.Rendering;

/// <summary>A page to render: the output path, the root component, and its parameters.</summary>
internal sealed record PageRequest(string FilePath, Type Component, IReadOnlyDictionary<string, object?> Parameters)
{
    public static PageRequest Create<TComponent>(string filePath, IReadOnlyDictionary<string, object?> parameters)
        where TComponent : IComponent => new(filePath, typeof(TComponent), parameters);
}
