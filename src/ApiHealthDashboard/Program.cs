using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Parsing;
using ApiHealthDashboard.Scheduling;
using ApiHealthDashboard.Services;
using ApiHealthDashboard.State;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables(prefix: "APIHEALTHDASHBOARD_");

// Add services to the container.
builder.Services.Configure<DashboardBootstrapOptions>(
    builder.Configuration.GetSection(DashboardBootstrapOptions.SectionName));
builder.Services.AddSingleton<DashboardConfigValidator>();
builder.Services.AddSingleton<IYamlConfigLoader, YamlConfigLoader>();
builder.Services.AddSingleton(static serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<DashboardBootstrapOptions>>().Value;
    var environment = serviceProvider.GetRequiredService<IHostEnvironment>();
    var loader = serviceProvider.GetRequiredService<IYamlConfigLoader>();
    var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("ApiHealthDashboard.Configuration");

    var resolvedPath = ResolveConfigPath(options.ResolveDashboardConfigPath(), environment.ContentRootPath);
    logger.LogInformation(
        "Loading dashboard configuration from {ConfigPath}.",
        resolvedPath);

    try
    {
        var loadResult = loader.Load(resolvedPath);

        foreach (var warning in loadResult.Warnings)
        {
            logger.LogWarning("{ConfigurationWarning}", warning);
        }

        logger.LogInformation(
            "Loaded dashboard configuration from {ConfigPath} with {EndpointCount} endpoints.",
            resolvedPath,
            loadResult.Config.Endpoints.Count);

        return loadResult;
    }
    catch (Exception ex)
    {
        logger.LogCritical(
            ex,
            "Failed to load dashboard configuration from {ConfigPath}.",
            resolvedPath);
        throw;
    }
});
builder.Services.AddSingleton(static serviceProvider =>
{
    var loadResult = serviceProvider.GetRequiredService<DashboardConfigLoadResult>();
    return new ConfigurationWarningState(loadResult.Warnings);
});
builder.Services.AddSingleton(static serviceProvider =>
    serviceProvider.GetRequiredService<DashboardConfigLoadResult>().Config);
builder.Services.AddSingleton<IEndpointStateStore>(static serviceProvider =>
{
    var config = serviceProvider.GetRequiredService<DashboardConfig>();
    var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("ApiHealthDashboard.State");
    var store = new InMemoryEndpointStateStore(config.Endpoints);

    logger.LogInformation(
        "Initialized endpoint state store with {EndpointCount} configured endpoints.",
        store.GetAll().Count);

    return store;
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient(nameof(EndpointPoller));
builder.Services.AddSingleton<IEndpointPoller, EndpointPoller>();
builder.Services.AddSingleton<IEndpointImportService, EndpointImportService>();
builder.Services.AddSingleton<IHealthResponseParser, HealthResponseParser>();
builder.Services.AddSingleton<PollingSchedulerService>();
builder.Services.AddSingleton<IEndpointScheduler>(static serviceProvider =>
    serviceProvider.GetRequiredService<PollingSchedulerService>());
builder.Services.AddHostedService(static serviceProvider =>
    serviceProvider.GetRequiredService<PollingSchedulerService>());
builder.Services.AddRazorPages();

var app = builder.Build();

app.Logger.LogInformation(
    "Starting ApiHealthDashboard in {EnvironmentName} environment with content root {ContentRoot}.",
    app.Environment.EnvironmentName,
    app.Environment.ContentRootPath);

try
{
    var dashboardConfig = app.Services.GetRequiredService<DashboardConfig>();
    _ = app.Services.GetRequiredService<IEndpointStateStore>();
    _ = app.Services.GetRequiredService<IEndpointPoller>();
    _ = app.Services.GetRequiredService<IHealthResponseParser>();
    _ = app.Services.GetRequiredService<IEndpointScheduler>();

    app.Logger.LogInformation(
        "Startup initialization completed for {EndpointCount} configured endpoints.",
        dashboardConfig.Endpoints.Count);
}
catch (Exception ex)
{
    app.Logger.LogCritical(ex, "Startup initialization failed.");
    throw;
}

app.Lifetime.ApplicationStarted.Register(() =>
{
    app.Logger.LogInformation(
        "ApiHealthDashboard is accepting requests in {EnvironmentName}.",
        app.Environment.EnvironmentName);
});

app.Lifetime.ApplicationStopping.Register(() =>
{
    app.Logger.LogInformation("ApiHealthDashboard is stopping.");
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();

app.Run();

static string ResolveConfigPath(string configuredPath, string contentRootPath)
{
    var configPath = string.IsNullOrWhiteSpace(configuredPath)
        ? "endpoints.yaml"
        : configuredPath;

    return Path.IsPathRooted(configPath)
        ? Path.GetFullPath(configPath)
        : Path.GetFullPath(Path.Combine(contentRootPath, configPath));
}
