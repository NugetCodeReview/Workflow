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
    public static int Main() => Execute<Build>(x => x.Compile);

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
            var dirs = GlobDirectories(RootDirectory / "src", "**/bin", "**/obj", "**/publish");
            dirs.ForEach(d =>
            {
                try
                {
                    Directory.Delete(d, true);
                }
                catch (Exception ex)
                {
                    failed.Add(ex);
                    Serilog.Log.Error(ex, $"While deleting {d}");
                }
            });

            var files = (RootDirectory / "artifacts").GlobFiles("*.zip");
            files.ForEach(f =>
            {
                try
                {
                    File.Delete(f);
                }
                catch (Exception ex)
                {
                    failed.Add(ex);
                    Serilog.Log.Error(ex, $"While deleting {f}");
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

            var project = RootDirectory / "src" / "Console" / "Workflow.csproj";
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

            CompressionTasks.CompressZip(ArtifactsDirectory / "workflow", ArtifactsDirectory / "workflow.zip");
            Directory.Delete(ArtifactsDirectory / "workflow", true);
        });
    Target PsModule => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            var destination = SourceDirectory / "Workflow.Commands" / "bin" / Configuration;
            var wfc = destination.GlobFiles("**/Workflow-Commands.dll").FirstOrDefault();

            if(wfc is not null)
            {
                destination = (AbsolutePath)Path.GetDirectoryName(wfc.ToString());
            }

            if (!Directory.Exists(destination))
            {
                var subfolders = Directory.GetDirectories(
                    SourceDirectory / "Workflow.Commands" / "bin");

                throw new ApplicationException(
                    $"Cannot find directory at [{destination}].  " +
                    $"{SourceDirectory / "Workflow.Commands" / "bin"} " +
                    $"has subfolders " +
                    $"{string.Join(", ", subfolders)}");
            }

            Action<AbsolutePath> CopyFile = file =>
            {
                try
                {
                    CopyFileToDirectory(
                                file,
                                destination,
                                FileExistsPolicy.Overwrite,
                                false);
                }
                catch
                {
                    Serilog.Log.Warning($"Could not copy {file}");
                }
            };

            (SourceDirectory / "Console").GlobFiles(
                $"**/bin/{Configuration}/**/*.dll",
                $"**/bin/{Configuration}/**/*.pdb"
            ).ForEach(CopyFile);


            CopyDirectoryRecursively(
                destination,
                ArtifactsDirectory / "PS",
                DirectoryExistsPolicy.Merge,
                FileExistsPolicy.OverwriteIfNewer);

            var ps = (ArtifactsDirectory / "PS").GlobFiles("*");

            if(ps.Count is 0)
            {
                throw new ApplicationException($"PS Module not published to {ArtifactsDirectory}");
            }

            CompressionTasks.CompressZip(ArtifactsDirectory / "PS", ArtifactsDirectory / "powershell.zip");
            Directory.Delete(ArtifactsDirectory / "PS", true);
        });

}
