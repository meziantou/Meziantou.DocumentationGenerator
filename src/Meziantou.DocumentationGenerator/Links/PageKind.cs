namespace Meziantou.DocumentationGenerator.Links;

internal enum PageKind
{
    Home,
    ApiReference,
    Package,
    Namespace,
    Type,
    Article,
    NotFound,

    /// <summary>A page redirecting a folder URL to the first page of the folder.</summary>
    Redirect,
    Asset,
}
