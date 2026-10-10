namespace Meziantou.DocumentationGenerator.Packages;

internal sealed class PackageArchiveException : Exception
{
    public PackageArchiveException()
    {
    }

    public PackageArchiveException(string message)
        : base(message)
    {
    }

    public PackageArchiveException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
