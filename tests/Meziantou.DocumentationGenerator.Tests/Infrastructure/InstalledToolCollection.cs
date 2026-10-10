namespace Meziantou.DocumentationGenerator.Tests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class InstalledToolCollection : ICollectionFixture<InstalledToolFixture>
{
    public const string Name = "Installed tool";
}
