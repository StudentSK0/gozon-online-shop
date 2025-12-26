using System;
using System.IO;
using System.Text.Json.Serialization;
using Gozon.Orders.Api.Background;
using Gozon.Orders.Api.Realtime;
using Gozon.Orders.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var xmlFile = $"{typeof(Program).Assembly.GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
});

var connectionString = builder.Configuration.GetSection("Database")["ConnectionString"]
    ?? "Host=localhost;Port=5432;Database=gozon_orders;Username=postgres;Password=postgres";

builder.Services.AddSingleton(new OrdersStore(connectionString));
builder.Services.AddSingleton<OrderStatusSocketManager>();
builder.Services.AddSingleton<OrderStatusPublisher>();
builder.Services.AddHostedService<OrdersOutboxPublisher>();
builder.Services.AddHostedService<PaymentResultConsumer>();
builder.Services.AddHostedService<OrderStatusConsumer>();

var app = builder.Build();

var store = app.Services.GetRequiredService<OrdersStore>();
await store.InitializeAsync();

app.UseSwagger();
app.UseSwaggerUI();

app.UseWebSockets();

app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    var userId = context.Request.Query["userId"].ToString();
    if (string.IsNullOrWhiteSpace(userId))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    var orderId = context.Request.Query["orderId"].ToString();
    var socketManager = context.RequestServices.GetRequiredService<OrderStatusSocketManager>();
    var ordersStore = context.RequestServices.GetRequiredService<OrdersStore>();

    var socket = await context.WebSockets.AcceptWebSocketAsync();
    var connectionId = socketManager.Add(socket, userId, orderId);

    if (!string.IsNullOrWhiteSpace(orderId))
    {
        var existing = await ordersStore.GetOrderAsync(orderId);
        if (existing != null && string.Equals(existing.UserId, userId, StringComparison.Ordinal))
        {
            await socketManager.SendToConnectionAsync(connectionId, OrderStatusUpdate.FromOrder(existing), context.RequestAborted);
        }
    }

    await socketManager.ListenAsync(connectionId, socket, context.RequestAborted);
});

app.MapControllers();

app.Run();

/// <summary>
/// Входная точка Orders API для поддержки XML-комментариев.
/// </summary>
public partial class Program { }
