var builder = Host.CreateApplicationBuilder(args);

// Hosted services (scheduler, consumers, alert dispatcher, rollup) arrive in M4 and M5.
await builder.Build().RunAsync();