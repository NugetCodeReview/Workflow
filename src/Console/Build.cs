
namespace Workflow;

using Workflow.Shared;

using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;

[NoBuildBanner]
class Build : NukeBuild
{
    public AbsolutePath ResultsDirectory => RootDirectory / "results";

    public static int Main() => Execute<Build>(x => x.GetTop100);

    protected override void OnBuildInitialized()
    {
        HostAppBuilder.BuildAppHost(RootDirectory);

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

            if (Force || !JsonManager.TodayFile.Exists)
            {
                List<PackageListing> results = new();

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

                List<PackageListing> results = (await JsonManager.TodayFile
                                         .FullName
                                         .DeserializeObject<List<PackageListing>>())!;

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
            Func<PackageListing, bool> packageFilter = p
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

    Target AddWorkflowTop100 => _ => _
        .DependsOn(GetTop100)
        .Executes(async () =>
        {
            Func<ContentItem, bool> fileFilter = i
                => i.Name.Equals("code-review.yml", StringComparison.OrdinalIgnoreCase);

            Func<PackageListing, bool> packageFilter = p
                => Force || !p.IsWhitelisted && p.Workflows.SingleOrDefault(fileFilter) is null;

            foreach (var pkg in Top100.Where(packageFilter))
            {
                await pkg.AddWorkflow();
                await pkg.CreateDocs();
                await Top100.Save();
            }
        });

    public List<PackageListing> Top100 { get; private set; }
}
