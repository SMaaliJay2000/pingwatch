using System.Threading.Channels;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using PingWatch.Application.Checks;
using PingWatch.Infrastructure;
using PingWatch.Infrastructure.Checks;
using PingWatch.Worker;

// A web host only so the health endpoints can be served.
var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

builder.Services.AddInfrastructure(connectionString).AddCheckEngine();

builder.Services.AddOptions<WorkerOptions>()
    .BindConfiguration(WorkerOptions.Section)
    .Validate(o => o.Concurrency is >= 1 and <= 1000, "Worker:Concurrency must be between 1 and 1000.")
    .ValidateOnStart();

builder.Services.AddSingleton(provider => Channel.CreateBounded<CheckJob>(
    provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<WorkerOptions>>().Value.Concurrency));
builder.Services.AddSingleton<SchedulerHealth>();
builder.Services.AddSingleton<IDomainEventPublisher, LoggingEventPublisher>();
builder.Services.AddScoped<CheckResultProcessor>();
builder.Services.AddScoped<RollupJob>();

builder.Services.AddHostedService<RollupService>();
builder.Services.AddHostedService<SchedulerService>();
builder.Services.AddHostedService<CheckConsumerService>();
builder.Services.AddHostedService<SchedulerWatchdog>();

builder.Services.AddHealthChecks().AddCheck<SchedulerHealthCheck>("scheduler");

var app = builder.Build();

// Live: the process responds. Ready: the scheduler is also doing its job.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");

app.Run();
