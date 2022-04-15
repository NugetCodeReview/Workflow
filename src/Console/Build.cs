
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

    new Target Help => _ => _
        .Executes(() =>
        {
            Log.Information("Help...");
        });

    Target GetTop100 => _ => _
        .Executes(() =>
        {
            foreach(var package in NugetOrg.GetPackages(NugetOrg.GetHtmlForPackages(), true))
            {
                Log.Debug(package.ToString());
            }
        });

    Target DownloadTop100 => _ => _
        .DependsOn(GetTop100)
        .Executes(() =>
        {
        });


}
