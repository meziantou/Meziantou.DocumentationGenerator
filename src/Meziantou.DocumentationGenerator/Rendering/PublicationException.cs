namespace Meziantou.DocumentationGenerator.Rendering;

internal sealed class PublicationException : Exception
{
    public PublicationException()
    {
    }

    public PublicationException(string message)
        : base(message)
    {
    }

    public PublicationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
