using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Parsing;
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

    var resolvedPath = ResolveConfigPath(options.EndpointsConfigPath, environment.ContentRootPath);
    var config = loader.Load(resolvedPath);

    logger.LogInformation(
        "Loaded dashboard configuration from {ConfigPath} with {EndpointCount} endpoints.",
        resolvedPath,
        config.Endpoints.Count);

    return config;
});
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
builder.Services.AddHttpClient(nameof(EndpointPoller));
builder.Services.AddSingleton<IEndpointPoller, EndpointPoller>();
builder.Services.AddSingleton<IHealthResponseParser, HealthResponseParser>();
builder.Services.AddRazorPages();

var app = builder.Build();

_ = app.Services.GetRequiredService<DashboardConfig>();
_ = app.Services.GetRequiredService<IEndpointStateStore>();
_ = app.Services.GetRequiredService<IEndpointPoller>();
_ = app.Services.GetRequiredService<IHealthResponseParser>();

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
