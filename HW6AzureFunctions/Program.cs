using HW6AzureFunctions.CustomSettings;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices(services =>
    {
       services.AddApplicationInsightsTelemetryWorkerService();
       services.ConfigureFunctionsApplicationInsights();
    });

// Configure logging
builder.ConfigureLogging((context, b) =>
{
   // Force log level to warning SDK is generating a lot of
   // noise and doesn't appear to honor config even with the
   // following commented out line
   //b.AddConfiguration(context.Configuration);
   b.SetMinimumLevel(LogLevel.Warning);
   b.AddConsole();
});

// Configure settings locations
builder.ConfigureAppConfiguration((context, configurationBuilder) =>
{
   configurationBuilder
       .AddJsonFile($"appsettings.json", optional: true)
       .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true)
       .AddEnvironmentVariables();

   if (context.HostingEnvironment.IsDevelopment())
   {
      configurationBuilder
          .AddUserSecrets<Program>();
   }
});

// Configure dependency injection
builder.ConfigureServices((context, s) =>
{
   ConfigureServices(context, s);
   s.BuildServiceProvider();
});

var host = builder.Build();
using (host)
{
   await host.RunAsync();
}

/// <summary>
/// Register items to be injected
/// </summary>
static void ConfigureServices(HostBuilderContext context, IServiceCollection s)
{
   // Configure storage settings to use the same settings as the SDK
   StorageSettings storageSettings = new()
   {
      ConnectionString = context.Configuration.GetValue<string>("StorageConnectionString")
   };
   s.AddSingleton<IStorageSettings>(storageSettings);
}