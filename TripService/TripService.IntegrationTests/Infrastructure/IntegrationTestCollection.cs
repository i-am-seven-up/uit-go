using Xunit;

namespace TripService.IntegrationTests.Infrastructure;

[CollectionDefinition("Integration Tests")]
public class IntegrationTestCollection : ICollectionFixture<TripServiceWebApplicationFactory>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the ICollectionFixture<> interfaces.
}
