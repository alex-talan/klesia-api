using FluentAssertions;
using Klesia.Application.Abstractions;
using Klesia.Infrastructure;
using Klesia.Infrastructure.Persistence;
using Klesia.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace UnitTests;

public sealed class PersistenceTests
{
    [Fact]
    public void AddInfrastructureRegistersSqlAndUserPersistence()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:KlesiaDb"] = "Server=tcp:test.database.windows.net,1433;Database=KlesiaTest;Authentication=Active Directory Default"
            })
            .Build();

        services.AddKlesiaOptions(configuration);
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        provider.GetService<ISqlConnectionFactory>().Should().NotBeNull();

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetService<IUserRepository>().Should().BeOfType<UserRepository>();

        using var connection = provider.GetRequiredService<ISqlConnectionFactory>().CreateConnection();
        connection.Should().BeOfType<Microsoft.Data.SqlClient.SqlConnection>();
        connection.State.Should().Be(System.Data.ConnectionState.Closed);
    }
}
