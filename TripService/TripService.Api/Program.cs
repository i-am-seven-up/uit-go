using Driver;
using Messaging.Contracts.Routing;
using Messaging.RabbitMQ;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
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
builder.Services.AddScoped<ITripRepository, EfTripRepository>();
builder.Services.AddScoped<ITripService, TripService.Application.Services.TripService>();

builder.Services.AddRabbitMqEventBus(builder.Configuration, Routing.Exchange);
builder.Services.AddScoped<TripMatchService>();
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis"))
);

builder.Services.AddScoped<IOfferStore, RedisOfferStore>();

builder.Services.AddHostedService<TripOfferDeclinedConsumer>();
builder.Services.AddHostedService<TripOfferedConsumer>();

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

app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
    db.Database.Migrate();
}

app.Run();
