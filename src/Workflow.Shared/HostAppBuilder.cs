using Microsoft.Extensions.Options;

using System.Diagnostics;

namespace Workflow.Shared;

public static class HostAppBuilder
{
    public static IHost AppHost { get; private set; } = null!;

    public static IHost BuildAppHost()
    {
        try
        {
            if (AppHost is not null) return AppHost;

            var location = Path.GetDirectoryName(typeof(HostAppBuilder).Assembly.Location);
            Serilog.Log.Information($"location: {location}");

            CreateLogger();
            var builder = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder();

            builder.ConfigureHostConfiguration(host =>
            {
#if !DEBUG
        host.AddJsonFile(Path.Combine(location!,"appsettings.json"), false);
#else
                host.AddJsonFile(Path.Combine(location!,"appsettings.Debug.json"), false);
                host.AddUserSecrets(Assembly.GetExecutingAssembly());
#endif

                host.AddEnvironmentVariables();
            });

            builder.ConfigureAppConfiguration(host =>
            {
#if !DEBUG
        host.AddJsonFile(Path.Combine(location!,"appsettings.json"), false);
#else
                host.AddJsonFile(Path.Combine(location!, "appsettings.Debug.json"), false);
#endif

                host.AddEnvironmentVariables();
            });

            builder.ConfigureLogging(loggingBuilder =>
            {
                loggingBuilder.AddSimpleConsole();
                loggingBuilder.AddSerilog();
            });

            builder.ConfigureServices(collection =>
            {
                collection.AddTransient<HttpClient>(provider =>
                {
                    var config = provider.GetRequiredService<IConfiguration>();
                    var client = new HttpClient();
                    client.DefaultRequestHeaders.Add("User-Agent", "Awesome-Octocat-App");
                    client.DefaultRequestHeaders.Add("Authorization", $"token {config["GITHUB_TOKEN"]}");
                    return client;
                });
                collection.AddTransient(services =>
                {
                    var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    var configuration = Options.Create(new PackagesConfig()).Value;
                    services.GetService<IConfiguration>()?.GetSection("Packages").Bind(configuration);
                    configuration.ConfigFolder = configuration.ConfigFolder?.Replace("~", userProfile)
                                                    ?? GetDefaultConfigFolder();
                    return configuration;
                });
            });

            return AppHost = builder.Build();
        }
        catch(Exception ex)
        {
            Console.Error.WriteLine(ex);
            throw;
        }
    }

    private static string GetDefaultConfigFolder()
        => Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile),
            ".nugetCodeReview");

    private static void CreateLogger()
    {
        string path = Path.Combine(GetDefaultConfigFolder(), "Logs");

        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Creating the logging directory failed: {ex}");
        }

        AssemblyName name = Assembly.GetExecutingAssembly()
            .GetName();
        const string OUTPUT_TEMPLATE = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";

        Log.Logger = new LoggerConfiguration().MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Assembly", $"{name.Name}")
            .Enrich.WithProperty("Version", $"{name.Version}")
            .WriteTo.Console(outputTemplate: OUTPUT_TEMPLATE, theme: AnsiConsoleTheme.Code)
            .WriteTo.Async(
                a => a.File(
                    $@"{path}/prime.log",
                    outputTemplate: OUTPUT_TEMPLATE,
                    rollingInterval: RollingInterval.Day,
                    shared: true
                )
            )
            .WriteTo.Async(
                a => a.File(
                    new JsonFormatter(),
                    $@"{path}/prime.json",
                    rollingInterval: RollingInterval.Day
                )
            )
            .CreateLogger();
    }
}