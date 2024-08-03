using HW6NoteKeeper;
using HW6NoteKeeper.Database;
using HW6NoteKeeper.Settings;
using HW6NoteKeeper.DataTransferObjects;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Extensibility.PerfCounterCollector.QuickPulse;
using Microsoft.ApplicationInsights.SnapshotCollector;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Filters;
using System.Reflection;
using Microsoft.Extensions.Azure;

var builder = WebApplication.CreateBuilder(args);

// Retrieve the connection string from the configuration
string dbConnectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
string storageConnectionString = builder.Configuration.GetConnectionString("DefaultStorageConnection") ?? string.Empty;

ApplicationInsights applicationInsightsSettings = builder.Configuration.GetSection(key: nameof(ApplicationInsights)).Get<ApplicationInsights>() ?? new ApplicationInsights();

ApplicationInsightsServiceOptions aiOptions = new()
{
   // Configures adaptive sampling, disabling adaptive sampling allows all events to be recorded
   EnableAdaptiveSampling = applicationInsightsSettings.EnableAdaptiveSampling,

   // Configure the connection string which provides the key necessary to connect to the application insights instance
   ConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]
};

// Setup app insights
builder.Services.AddApplicationInsightsTelemetry(aiOptions);

// Register custom TelemetryInitializer to provide role name when running locally
builder.Services.AddSingleton<ITelemetryInitializer, DevelopmentRoleNameTelemetryInitializer>();

// Setup live monitoring key so authentication is enabled allowing filtering of events
builder.Services.ConfigureTelemetryModule<QuickPulseTelemetryModule>((module, _) =>
{
    module.AuthenticationApiKey = applicationInsightsSettings.AuthenticationApiKey;
});

// Allows for custom metrics and events to be sent
builder.Services.AddSingleton(typeof(TelemetryClient));


// Setup live monitoring key so authentication is enabled allowing filtering of events
builder.Services.ConfigureTelemetryModule<QuickPulseTelemetryModule>((module, _) => module.AuthenticationApiKey = applicationInsightsSettings.AuthenticationApiKey);

builder.Services.AddSingleton(typeof(TelemetryClient));

builder.Services.AddSnapshotCollector((configuration)
    => builder.Configuration
    .Bind(nameof(SnapshotCollectorConfiguration), configuration));

// Configure logging for the development environment
if (builder.Environment.IsDevelopment())
{
    builder.Logging.AddConsole();
}

// Add services for controllers and API explorer
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Configure Swagger generation
builder.Services.AddSwaggerGen(c =>
{
    // Specify the XML file containing the documentation comments
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Note Keeper", Version = "v1" });
    c.ExampleFilters();
    c.IncludeXmlComments(xmlPath);
});

// Add Swagger examples from the assembly of NoteCreatePayload
builder.Services.AddSwaggerExamplesFromAssemblyOf<NoteCreatePayload>();

// Add the DatabaseContext to the services with the specified SQL Server connection string
builder.Services.AddDbContext<DatabaseContext>(options => options.UseSqlServer(dbConnectionString));

// Bind the configuration section for noteSettings and add it as a singleton service
NoteSettings noteSettings = new();
builder.Configuration.GetSection(nameof(NoteSettings)).Bind(noteSettings);
builder.Services.AddSingleton(implementationInstance: noteSettings);
builder.Services.AddSingleton(implementationInstance: storageConnectionString);

// Add the class that represents the settings
LimitSettings limitSettings = new();
builder.Configuration.GetSection(nameof(LimitSettings)).Bind(limitSettings);
builder.Services.AddSingleton(implementationInstance: limitSettings);
builder.Services.AddAzureClients(clientBuilder =>
{
    clientBuilder.AddBlobServiceClient(builder.Configuration["ConnectionStrings:DefaultStorageConnection:blob"]!, preferMsi: true);
    clientBuilder.AddQueueServiceClient(builder.Configuration["ConnectionStrings:DefaultStorageConnection:queue"]!, preferMsi: true);
});



// Build the application
var app = builder.Build();

// Seed the database with initial data
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<DatabaseContext>();
        DbInitializer.Initialize(context, storageConnectionString);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "*** ERROR *** An error occurred while seeding the database. *** ERROR ***");
    }
}

// Configure Swagger UI
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.DocumentTitle = "NoteKeeper - Swagger docs";
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "v1");
    c.EnableDeepLinking();
    c.DefaultModelsExpandDepth(0);
    c.RoutePrefix = string.Empty;
});

// Enable HTTPS redirection and authorization
app.UseHttpsRedirection();
app.UseAuthorization();

// Map the controllers
app.MapControllers();

// Run the application
app.Run();