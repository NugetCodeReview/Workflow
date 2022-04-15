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

            CreateLogger();
            var builder = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder();

            builder.ConfigureHostConfiguration(host =>
            {
#if !DEBUG
        host.AddJsonFile(Path.Combine(location!,"appsettings.json"), false);
#else
                host.AddJsonFile(Path.Combine(location!,"appsettings.Debug.json"), false);
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
                collection.AddTransient<HttpClient>();
            });

            return AppHost = builder.Build();
        }
        catch(Exception ex)
        {
            Console.Error.WriteLine(ex);
            throw;
        }
    }

    private static void CreateLogger()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".homeschool"
        );

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