using System;
using System.IO;
using System.Text.Json.Serialization;
using Gozon.Payments.Api.Background;
using Gozon.Payments.Infrastructure.Persistence;

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
    ?? "Host=localhost;Port=5432;Database=gozon_payments;Username=postgres;Password=postgres";

builder.Services.AddSingleton(new PaymentsStore(connectionString));
builder.Services.AddHostedService<PaymentsOutboxPublisher>();
builder.Services.AddHostedService<PaymentRequestConsumer>();

var app = builder.Build();

var store = app.Services.GetRequiredService<PaymentsStore>();
await store.InitializeAsync();

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

app.Run();

/// <summary>
/// Входная точка Payments API для поддержки XML-комментариев.
/// </summary>
public partial class Program { }
