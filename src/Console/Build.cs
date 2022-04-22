
namespace Workflow;

using Workflow.Shared;

using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;

[NoBuildBanner]
class Build : NukeBuild
{
    private static PackagesConfig _config;

    public AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    public AbsolutePath ResultsDirectory => ArtifactsDirectory / "results";

#if !DEBUG
    public AbsolutePath BinDirectory => ArtifactsDirectory / "bin";
#else
    public AbsolutePath BinDirectory => (AbsolutePath)Path.GetDirectoryName(typeof(HostAppBuilder).Assembly!.Location!);
#endif

    public static int Main() => Execute<Build>(x => x.GetTop100);

    protected override void OnBuildInitialized()
    {
        Log.Information($"ArtifactsDirectory: {ArtifactsDirectory}");
        Log.Information($"ResultsDirectory: {ResultsDirectory}");
        Log.Information($"BinDirectory: {BinDirectory}");
        HostAppBuilder.BuildAppHost(BinDirectory, ResultsDirectory, ArtifactsDirectory);

        var config = HostAppBuilder.AppHost!.Services.GetRequiredService<PackagesConfig>();

        config.ConfigFolder = ResultsDirectory / "config";
        config.DownloadFolder = ResultsDirectory / "cache";
    }

    protected override void OnBuildFinished() { }

    [Parameter]
    bool Force { get; set; }

    new Target Help => _ => _
        .Executes(() =>
        {
            Log.Information("Help...");
        });

    Target GetTop100 => _ => _
        .Executes(async () =>
        {
            var config = HostAppBuilder.AppHost!.Services.GetRequiredService<PackagesConfig>();

            List<PackageHeader> results = PackageHeader.AllHeaders;

            if (Force || results is null or { Count: 0 })
            {
                results = new();

                var html = NugetOrg.GetHtmlForPackages();
                Log.Information($"html length: {html.Length}");

                foreach (var package in NugetOrg.GetPackages(html, true))
                {
                    results.Add(package);
                    Log.Information(package.ToString());
                }

                if (await results.Save())
                {
                    Log.Debug($"Saved Json. {JsonManager.TodayFile}");

                    Top100 = results;
                }
                else
                {
                    Log.Error($"Failed to save Json. {JsonManager.TodayFile}");
                }
            }
            else
            {
                Log.Debug($"Using Existing Json. {JsonManager.TodayFile}");

                results = (await JsonManager.TodayFile
                                         .FullName
                                         .DeserializeObject<List<PackageHeader>>())!;

                Top100 = results;

                foreach (var package in results)
                {
                    Log.Information(package.ToString());
                }
            }
        });

    Target ForkTop100 => _ => _
        .DependsOn(GetTop100)
        .Executes(async () =>
        {
            Func<PackageHeader, bool> packageFilter = p
                => Force || !p.IsWhitelisted && p.ForkUrl is null;

            foreach (var package in Top100.Where(packageFilter))
            {
                var uri = await package.ForkPackageRepositoryAsync();
                if (uri is { Length: > 0 })
                {
                    Log.Information($"Package {package.PackageName} forked to {uri}");
                }
                else
                {
                    Log.Warning($"Could not fork {package.PackageName}");
                }

                if (package.ForkUrl is not null)
                {
                    await Top100.Save();
                }
            }
        });

    [JsonIgnore]
    private static PackagesConfig Config
        => _config ??= HostAppBuilder.AppHost.Services.GetRequiredService<PackagesConfig>();

    Target AddWorkflowTop100 => _ => _
        .DependsOn(ForkTop100)
        .Executes(async () =>
        {
            Func<ContentItem, bool> fileFilter = i
                => i.Name.Equals("code-review.yml", StringComparison.OrdinalIgnoreCase);

            Func<PackageHeader, bool> packageFilter = p
                => Force || !p.IsWhitelisted && p.Workflows.SingleOrDefault(fileFilter) is null;

            var filtered = Top100.Where(packageFilter);

            foreach (var pkg in filtered)
            {
                var packageDirectory =
                    Path.Combine(Config.DownloadFolder!, JsonManager.TodayString, pkg.PackageName!);

                await pkg.AddWorkflow();
                await pkg.CreateDocs();
                await Top100.Save();
                await pkg.SaveAsync(packageDirectory);
            }
        });

    public List<PackageHeader> Top100 { get; private set; }
}
