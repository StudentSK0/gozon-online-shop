var builder = WebApplication.CreateBuilder(args);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.MapReverseProxy();

app.Run();

/// <summary>
/// Входная точка API Gateway для поддержки XML-комментариев.
/// </summary>
public partial class Program { }
