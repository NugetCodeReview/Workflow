
namespace Workflow;

using Workflow.Shared;

using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;

[NoBuildBanner]
class Build : NukeBuild
{
    public static int Main() => Execute<Build>(x => x.GetTop100);

    protected override void OnBuildInitialized()
    {
        HostAppBuilder.BuildAppHost();
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

                foreach(var package in results)
                {
                    Log.Information(package.ToString());
                }
            }
        });

    Target DownloadTop100 => _ => _
        .DependsOn(GetTop100)
        .Executes(() =>
        {
        });


}
