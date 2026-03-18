using ApiHealthDashboard.Cli;
using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Parsing;
using ApiHealthDashboard.Scheduling;
using ApiHealthDashboard.Services;
using ApiHealthDashboard.State;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
var cliParseResult = CliOptions.Parse(args);

if (cliParseResult.IsCliMode)
{
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(options =>
    {
        options.LogToStandardErrorThreshold = LogLevel.Trace;
        options.FormatterName = ConsoleFormatterNames.Simple;
    });
}

builder.Configuration.AddEnvironmentVariables(prefix: "APIHEALTHDASHBOARD_");

// Add services to the container.
builder.Services.Configure<DashboardBootstrapOptions>(
    builder.Configuration.GetSection(DashboardBootstrapOptions.SectionName));
builder.Services.Configure<ImportUiOptions>(
    builder.Configuration.GetSection(ImportUiOptions.SectionName));
builder.Services.Configure<RuntimeStateOptions>(
    builder.Configuration.GetSection(RuntimeStateOptions.SectionName));
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
    serviceProvider.GetRequiredService<DashboardConfigLoadResult>().Config.Clone());
builder.Services.AddSingleton<IEndpointStateStore>(static serviceProvider =>
{
    var config = serviceProvider.GetRequiredService<DashboardConfig>();
    var environment = serviceProvider.GetRequiredService<IHostEnvironment>();
    var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("ApiHealthDashboard.State");
    var runtimeStateOptions = serviceProvider.GetRequiredService<IOptions<RuntimeStateOptions>>().Value;

    if (!runtimeStateOptions.Enabled)
    {
        var inMemoryStore = new InMemoryEndpointStateStore(config.Endpoints);

        logger.LogInformation(
            "Initialized in-memory endpoint state store with {EndpointCount} configured endpoints.",
            inMemoryStore.GetAll().Count);

        return inMemoryStore;
    }

    var resolvedStateDirectory = runtimeStateOptions.ResolveDirectoryPath(environment.ContentRootPath);
    var fileBackedStore = new FileBackedEndpointStateStore(
        config.Endpoints,
        resolvedStateDirectory,
        runtimeStateOptions,
        serviceProvider.GetRequiredService<ILogger<FileBackedEndpointStateStore>>());

    logger.LogInformation(
        "Initialized file-backed endpoint state store with {EndpointCount} configured endpoints in {StateDirectoryPath}.",
        fileBackedStore.GetAll().Count,
        resolvedStateDirectory);

    return fileBackedStore;
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient(nameof(EndpointPoller));
builder.Services.AddSingleton<IEndpointPoller, EndpointPoller>();
builder.Services.AddSingleton<IEndpointImportService, EndpointImportService>();
builder.Services.AddSingleton<IHealthResponseParser, HealthResponseParser>();
builder.Services.AddSingleton<CliExecutionService>();
builder.Services.AddSingleton<PollingSchedulerService>();
builder.Services.AddSingleton<IEndpointScheduler>(static serviceProvider =>
    serviceProvider.GetRequiredService<PollingSchedulerService>());
builder.Services.AddHostedService(static serviceProvider =>
    serviceProvider.GetRequiredService<PollingSchedulerService>());
builder.Services.AddSingleton<DashboardConfigHotReloadService>();
builder.Services.AddHostedService(static serviceProvider =>
    serviceProvider.GetRequiredService<DashboardConfigHotReloadService>());
builder.Services.AddRazorPages();

await using var app = builder.Build();

if (cliParseResult.IsCliMode)
{
    if (cliParseResult.IsHelpRequested)
    {
        Console.WriteLine(CliOptions.GetHelpText());
        return;
    }

    if (!cliParseResult.IsValid || cliParseResult.Options is null)
    {
        Console.Error.WriteLine(cliParseResult.ErrorMessage ?? "Invalid CLI arguments.");
        Console.Error.WriteLine();
        Console.Error.WriteLine(CliOptions.GetHelpText());
        Environment.ExitCode = 2;
        return;
    }

    try
    {
        var cliOptions = cliParseResult.Options;
        var bootstrapOptions = app.Services.GetRequiredService<IOptions<DashboardBootstrapOptions>>().Value;
        var yamlLoader = app.Services.GetRequiredService<IYamlConfigLoader>();
        var httpClientFactory = app.Services.GetRequiredService<IHttpClientFactory>();
        var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
        var timeProvider = app.Services.GetRequiredService<TimeProvider>();

        var resolvedDashboardPath = ResolveConfigPath(
            string.IsNullOrWhiteSpace(cliOptions.DashboardConfigPathOverride)
                ? bootstrapOptions.ResolveDashboardConfigPath()
                : cliOptions.DashboardConfigPathOverride,
            app.Environment.ContentRootPath);

        var loadResult = cliOptions.RunAll
            ? yamlLoader.Load(resolvedDashboardPath)
            : yamlLoader.LoadSelectedEndpoints(resolvedDashboardPath, cliOptions.EndpointFiles);

        var poller = new EndpointPoller(
            httpClientFactory,
            loadResult.Config,
            loggerFactory.CreateLogger<EndpointPoller>());
        var parser = new HealthResponseParser(loggerFactory.CreateLogger<HealthResponseParser>());
        var cliExecutionService = new CliExecutionService(
            poller,
            parser,
            timeProvider,
            loggerFactory.CreateLogger<CliExecutionService>());
        var report = await cliExecutionService.ExecuteAsync(
            loadResult.Config,
            cliOptions,
            resolvedDashboardPath,
            loadResult.Warnings,
            CancellationToken.None);

        Console.WriteLine(CliReportSerializer.SerializeJson(report));

        if (!string.IsNullOrWhiteSpace(cliOptions.OutputFilePath))
        {
            var outputPath = ResolveOutputPath(cliOptions.OutputFilePath, app.Environment.ContentRootPath);
            await CliReportSerializer.WriteToFileAsync(
                report,
                outputPath,
                cliOptions.ResolveOutputFileFormat(),
                CancellationToken.None);
        }

        return;
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "CLI execution failed.");
        Console.Error.WriteLine(ex.Message);
        Environment.ExitCode = 1;
        return;
    }
}

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
        ? "dashboard.yaml"
        : configuredPath;

    return Path.IsPathRooted(configPath)
        ? Path.GetFullPath(configPath)
        : Path.GetFullPath(Path.Combine(contentRootPath, configPath));
}

static string ResolveOutputPath(string configuredPath, string contentRootPath)
{
    return Path.IsPathRooted(configuredPath)
        ? Path.GetFullPath(configuredPath)
        : Path.GetFullPath(Path.Combine(contentRootPath, configuredPath));
}
