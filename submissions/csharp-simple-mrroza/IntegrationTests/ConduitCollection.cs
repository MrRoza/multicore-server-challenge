using Xunit;

namespace IntegrationTests;

[CollectionDefinition(Name)]
public sealed class ConduitCollection : ICollectionFixture<ConduitApp>
{
    public const string Name = "Conduit";
}
