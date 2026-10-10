namespace Meziantou.DocumentationGenerator.Tests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class BrowserCollection : ICollectionFixture<BrowserFixture>
{
    public const string Name = "Browser";
}
