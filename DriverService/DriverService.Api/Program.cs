using DriverService.Api.Grpc;
using DriverService.Api.Messaging;
using DriverService.Application.Abstractions;
using DriverService.Application.Services;
using DriverService.Infrastructure.Data;
using DriverService.Infrastructure.Repositories;
using Messaging.Contracts.Routing;
using Messaging.RabbitMQ;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(8080, lo =>
    {
        lo.Protocols = HttpProtocols.Http2; // quan trọng
    });
});

builder.Services.AddControllers();
builder.Services.AddGrpc();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<DriverDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<IDriverRepository, EfDriverRepository>();
builder.Services.AddScoped<IDriverService, DriverService.Application.Services.DriverService>();

builder.Services.AddRabbitMqEventBus(builder.Configuration, Routing.Exchange);
builder.Services.AddHostedService<TripRequestedConsumer>();
builder.Services.AddHostedService<TripCreatedConsumer>();  
builder.Services.AddHostedService<TripAssignedConsumer>();
builder.Services.AddScoped<DriverLocationService>(); 
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis"))
);
builder.Services.AddHealthChecks();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.MapGrpcService<DriverQueryService>(); 

app.MapHealthChecks("/health");

// auto-migrate DB mỗi lần start
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DriverDbContext>();
    db.Database.Migrate();
}

app.Run();
