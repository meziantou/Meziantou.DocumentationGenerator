using System.Collections.Immutable;
using NuGet.Frameworks;

namespace Meziantou.DocumentationGenerator.Discovery;

/// <summary>
/// C# files of a source package for one target framework: <c>contentFiles/cs/{tfm}/**/*.cs</c> and
/// <c>contentFiles/any/{tfm}/**/*.cs</c>, which NuGet adds to the compilation of the consuming project.
/// </summary>
/// <param name="Framework">Target framework of the folder, <see cref="NuGetFramework.AnyFramework"/> for <c>any</c>.</param>
/// <param name="DisplayPath">Folder shown in diagnostics and used as the asset entry, such as <c>contentFiles/cs/net8.0/</c>.</param>
/// <param name="Entries">Package entries of the source files, sorted ordinally.</param>
internal sealed record SourceFolder(NuGetFramework Framework, string DisplayPath, ImmutableArray<string> Entries)
{
    public string TargetFramework => Framework.GetShortFolderName();
}
