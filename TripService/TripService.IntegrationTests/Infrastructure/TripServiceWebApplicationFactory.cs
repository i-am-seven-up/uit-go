using Microsoft.Extensions.Configuration;
using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;
using TripService.Infrastructure.Data;
using Messaging.RabbitMQ.Abstractions; // Add this for IEventConsumer
using Messaging.RabbitMQ; // Add this for AddRabbitMqEventBus extension method

namespace TripService.IntegrationTests.Infrastructure;

public class TripServiceWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer;
    private readonly RedisContainer _redisContainer;
    private readonly RabbitMqContainer _rabbitMqContainer;

    public TripServiceWebApplicationFactory()
    {
        _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("tripservice_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        _redisContainer = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();

        _rabbitMqContainer = new RabbitMqBuilder()
            .WithImage("rabbitmq:3-management-alpine")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Override configuration FIRST before services are configured
        builder.UseSetting("Jwt:Secret", "your-super-secret-key-minimum-32-characters-long");
        builder.UseSetting("Jwt:Issuer", "test-issuer");
        builder.UseSetting("Jwt:Audience", "test-audience");

        builder.ConfigureAppConfiguration((context, conf) =>
        {
            // Parse RabbitMQ connection string to get host and port
            var rabbitUri = new Uri(_rabbitMqContainer.GetConnectionString());

            conf.AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ConnectionStrings:Default", _postgresContainer.GetConnectionString() },
                { "ConnectionStrings:Redis", _redisContainer.GetConnectionString() },
                { "RabbitMQ:HostName", rabbitUri.Host },
                { "RabbitMQ:Port", rabbitUri.Port.ToString() },
                { "RabbitMQ:UserName", "test" },
                { "RabbitMQ:Password", "test" },
                { "RabbitMQ:VirtualHost", "/" },
                { "Jwt:Secret", "your-super-secret-key-minimum-32-characters-long" },
                { "Jwt:Issuer", "test-issuer" },
                { "Jwt:Audience", "test-audience" }
            });
        });

        builder.ConfigureTestServices(services => // Reverted to single argument lambda
        {
            // Remove existing DbContext
            services.RemoveAll<DbContextOptions<TripDbContext>>();
            services.RemoveAll<TripDbContext>();

            // Add test database
            services.AddDbContext<TripDbContext>(options =>
                options.UseNpgsql(
                    _postgresContainer.GetConnectionString(),
                    b => b.MigrationsAssembly("TripService.Infrastructure")));

            // Explicitly remove and re-add Redis connection to ensure it uses the test container.
            services.RemoveAll<IConnectionMultiplexer>();
            var redisConfig = ConfigurationOptions.Parse(_redisContainer.GetConnectionString());
            redisConfig.AbortOnConnectFail = false;
            redisConfig.AllowAdmin = true; // Enable admin mode for FLUSHDB in tests
            services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConfig));

            // RabbitMQ services are configured via IConfiguration (testcontainer connection string).
            // Program.cs will register IEventPublisher and real consumers.
            // We add MockTripCancelledConsumer to simulate DriverService's TripCancelledConsumer.
            services.AddHostedService<MockTripCancelledConsumer>();

            // Replace gRPC client with mock
            services.RemoveAll<Driver.DriverQuery.DriverQueryClient>();
            services.AddSingleton<Driver.DriverQuery.DriverQueryClient>(sp => new MockDriverGrpcClient(sp));

        });

        builder.UseEnvironment("Testing");
    }

    public async Task InitializeAsync()
    {
        await _postgresContainer.StartAsync();
        await _redisContainer.StartAsync();
        await _rabbitMqContainer.StartAsync();

        // Run migrations
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TripDbContext>();
        var connectionString = dbContext.Database.GetConnectionString();
        Console.WriteLine($"=== Applying migrations to: {connectionString} ===");

        try
        {
            var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync();
            Console.WriteLine($"Pending migrations: {string.Join(", ", pendingMigrations)}");

            await dbContext.Database.MigrateAsync(); // Apply pending migrations

            var appliedMigrations = await dbContext.Database.GetAppliedMigrationsAsync();
            Console.WriteLine($"Applied migrations: {string.Join(", ", appliedMigrations)}");
            Console.WriteLine("=== Migrations applied successfully ===");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"=== ERROR applying migrations: {ex.Message} ===");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            throw;
        }
    }

    public new async Task DisposeAsync()
    {
        await _postgresContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
        await _rabbitMqContainer.DisposeAsync();
        await base.DisposeAsync();
    }

    public IConnectionMultiplexer GetRedisConnection()
    {
        return Services.GetRequiredService<IConnectionMultiplexer>();
    }

    public TripDbContext GetDbContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<TripDbContext>();
    }

    public MockEventPublisher GetMockEventPublisher()
    {
        return Services.GetRequiredService<MockEventPublisher>();
    }

    public DriverSimulator GetDriverSimulator()
    {
        return new DriverSimulator(this);
    }
}
