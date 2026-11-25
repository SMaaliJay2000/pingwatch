using System.Text.Json;
using System.Text.Json.Serialization;
using PingWatch.Api;
using PingWatch.Api.Auth;
using PingWatch.Application.Accounts;
using PingWatch.Application.Auth;
using PingWatch.Infrastructure;
using PingWatch.Infrastructure.Email;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");
var appBaseUrl = builder.Configuration["App:BaseUrl"]
    ?? throw new InvalidOperationException("App:BaseUrl is not configured.");
var smtp = builder.Configuration.GetSection("Smtp").Get<SmtpOptions>()
    ?? throw new InvalidOperationException("Smtp is not configured.");

// Auth goes first: it registers the tenant context that AddInfrastructure would otherwise default.
builder.Services.AddPingWatchAuth(builder.Configuration);
builder.Services.AddInfrastructure(connectionString)
    .AddAccountServices()
    .AddSmtpEmail(smtp);

builder.Services.AddSingleton(new AuthLinkOptions(appBaseUrl));
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AccountService>();

builder.Services
    .AddControllers(options => options.Filters.Add<ValidationExceptionFilter>())
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapControllers();

app.Run();

// Exposed so WebApplicationFactory<Program> can boot the API in integration tests.
public partial class Program;
