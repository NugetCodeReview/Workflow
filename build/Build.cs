using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Build.Construction;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.Execution;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Tools.PowerShell;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;

class Build : NukeBuild
{
    public static int Main () => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    Configuration Configuration { get; set; } = Configuration.Debug;

    [Solution("Workflow.sln")] readonly Solution? Solution;
    [GitRepository] readonly GitRepository? GitRepository;
    [GitVersion] readonly GitVersion? GitVersion;

    AbsolutePath TestsDirectory => RootDirectory / "tests";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";

    AbsolutePath SourceDirectory => RootDirectory / "src";

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            List<Exception> failed = new();
            var dirs = GlobDirectories(RootDirectory / "src", "**/bin", "**/obj", "**/publish", "**/artifacts");
            dirs.ForEach(d =>
            {
                try
                {
                    Directory.Delete(d, true);
                }
                catch(Exception ex)
                {
                    failed.Add(ex);
                    Serilog.Log.Error(ex, $"While deleting {d}");
                }
            });

            if (failed.Count > 0)
            {
                throw new AggregateException(failed);
            }
        });

    Target Restore => _ => _
        .Executes(() =>
        {
            var settings = new DotNetRestoreSettings()
                .SetProjectFile(Solution)
                ;

            DotNetTasks.DotNetRestore(settings);
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            var settings = new DotNetBuildSettings()
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .SetNoRestore(true)
                ;

            DotNetTasks.DotNetBuild(settings);
        });
    Target Publish => _ => _
        .DependsOn(Clean)
        //.DependsOn(Restore)
        .Executes(() =>
        {
            var restoreSettings = new DotNetRestoreSettings()
                .SetProjectFile(Solution)
                .SetRuntime("linux-x64")
                ;

            DotNetTasks.DotNetRestore(restoreSettings);

            var project = RootDirectory / "src" / "console" / "Workflow.csproj";
            var settings = new DotNetPublishSettings()
                .SetProject(project)
                .SetConfiguration(Configuration)
                .SetNoRestore(true)
                .SetDeterministic(true)
                .SetRuntime("linux-x64")
                .SetSelfContained(true)
                .SetPublishReadyToRun(false)
                .SetPublishSingleFile(true)
                .SetOutput(ArtifactsDirectory / "workflow")
                ;

            DotNetTasks.DotNetPublish(settings);
        });
    Target PsModule => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            var source = GlobFiles(SourceDirectory / "Console",
                $"**/bin/{Configuration}/**/*.dll",
                $"**/bin/{Configuration}/**/*.pdb"
            );

            var destination = SourceDirectory / "Workflow.Commands" / "bin" / Configuration / "net6.0";

            foreach(var file in source)
            {
                if (File.Exists(file))
                {
                    CopyFileToDirectory(file, destination, FileExistsPolicy.OverwriteIfNewer, false);
                }
            }

            var f = $"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}/.nuget/packages/microsoft.bcl.asyncinterfaces/6.0.0/lib/netstandard2.0/Microsoft.Bcl.AsyncInterfaces.dll";
            if (File.Exists(f))
            {
                CopyFileToDirectory(
                    f,
                    destination,
                    FileExistsPolicy.Overwrite,
                    false);
            }
            else
            {
                Serilog.Log.Error($"missing: {f}");
            }

            CopyDirectoryRecursively(destination, ArtifactsDirectory / "PS", DirectoryExistsPolicy.Merge, FileExistsPolicy.OverwriteIfNewer);
        });

}
