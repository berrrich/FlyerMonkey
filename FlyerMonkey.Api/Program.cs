using FlyerMonkey.Api.Services;
using SQLServerConnection.Data;
using Microsoft.Data.SqlClient;

var builder = WebApplication.CreateBuilder(args);

var rawConnectionString =
    Environment.GetEnvironmentVariable(
        "FLYERMONKEY_SQL_CONNECTION")
    ?? throw new InvalidOperationException(
        "FLYERMONKEY_SQL_CONNECTION is not set.");

var connectionStringBuilder =
    new SqlConnectionStringBuilder(rawConnectionString)
    {
        ConnectTimeout = 5
    };

var sqlConnectionString =
    connectionStringBuilder.ConnectionString;

builder.Services.AddSingleton<IProductRepository>(
    new ProductRepository(sqlConnectionString));

builder.Services.AddScoped<ProductService>();

// Add services to the container.

builder.Services.AddControllers();

builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
