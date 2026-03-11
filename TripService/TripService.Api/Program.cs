using Driver;
using Messaging.Contracts.Routing;
using Messaging.RabbitMQ;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using System.Text;
using TripService.Api.Messaging;
using TripService.Application.Abstractions;
using TripService.Application.Offers;
using TripService.Application.Services;
using TripService.Infrastructure.Data;
using TripService.Infrastructure.Repositories;

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services
    .AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter()
        );
    });
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddDbContext<TripDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// JWT Authentication
var jwtSecret = builder.Configuration["Jwt:Secret"];
var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddScoped<ITripRepository, EfTripRepository>();
builder.Services.AddScoped<ITripService, TripService.Application.Services.TripService>();

builder.Services.AddRabbitMqEventBus(builder.Configuration, Routing.Exchange);
builder.Services.AddScoped<TripMatchService>();
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis"), options => options.AbortOnConnectFail = false)
);

builder.Services.AddScoped<IOfferStore, RedisOfferStore>();

// ✅ Phase 2: Add Timeout Scheduler (eliminates blocking Task.Delay)
builder.Services.AddScoped<TripOfferTimeoutScheduler>();

if (!builder.Environment.IsEnvironment("Testing")) // Skip hosted services in testing environment
{
    // Existing consumers
    builder.Services.AddHostedService<TripOfferDeclinedConsumer>();
    builder.Services.AddHostedService<TripOfferedConsumer>();
    builder.Services.AddHostedService<DriverAcceptedTripConsumer>();
    builder.Services.AddHostedService<DriverDeclinedTripConsumer>();

    // ✅ Phase 2: New consumers for non-blocking timeout handling
    builder.Services.AddHostedService<TripAutoAssignedConsumer>();
    builder.Services.AddHostedService<TripOfferTimeoutConsumer>();

    // ✅ Phase 2: Background worker that polls for expired timeouts
    builder.Services.AddHostedService<TripService.Api.BackgroundServices.OfferTimeoutWorker>();
}

builder.Services.AddGrpcClient<DriverQuery.DriverQueryClient>(o =>
{
    // khớp với service name "driver-service" trong docker-compose
    // và port nội bộ 8080 (vì DriverService listen http://+:8080)
    o.Address = new Uri("http://driver-service:8080");
});

builder.Services.AddHealthChecks();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health");

if (!app.Environment.IsEnvironment("Testing")) // Skip migrations in testing environment
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
        db.Database.Migrate();
    }
}

app.Run();

// Make the implicit Program class public for integration tests
public partial class Program { }
