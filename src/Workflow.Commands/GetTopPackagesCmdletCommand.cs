using System.Diagnostics;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using Newtonsoft.Json;

using Workflow.Shared;

namespace Workflow.Commands;

[Cmdlet(VerbsCommon.Get,"TopPackages")]
[OutputType(typeof(PackageListing))]
public class GetTopPackagesCmdletCommand : PSCmdlet
{
    static GetTopPackagesCmdletCommand()
    {
        try
        {
            HostAppBuilder.BuildAppHost();
        }
        catch(Exception ex)
        {
            Console.Error.WriteLine(ex);
            Log.Error(ex, "static GetTopPackagesCmdletCommand()");
        }
    }

    [Parameter(
        Mandatory = false,
        Position = 0,
        ValueFromPipeline = false,
        ValueFromPipelineByPropertyName = false)]
    public SwitchParameter IncludeDetails { get; set; }

    [Parameter(
        Mandatory = false,
        Position = 1,
        ValueFromPipeline = false,
        ValueFromPipelineByPropertyName = false)]
    public SwitchParameter Force { get; set; }

    protected SwitchParameter IsVerbose => (SwitchParameter)(GetVariableValue("Verbose") ?? GetVariableValue("$Verbose"));

    // This method gets called once for each cmdlet in the pipeline when the pipeline starts executing
    protected override void BeginProcessing()
    {
        WriteInformation(new InformationRecord($"IsVerbose: {IsVerbose}", GetType().Name));
        //if(IsVerbose)
        Log.Information($"Starting. IncludeDetails: {IncludeDetails}, Force: {Force}");
        NugetOrg.UpdateStatus += NugetOrg_UpdateStatus;
    }

    private void NugetOrg_UpdateStatus(int counter, int items, string status)
    {
        if (IsVerbose) WriteVerbose($"Completed {counter} of {items}: {status}");
        WriteProgress(new ProgressRecord(counter, status, $"Completed {counter} of {items}"));
        // Log.Information($"Completed {counter} of {items}: {status}");
    }

    // This method will be called for each input received from the pipeline to this cmdlet; if no input is received, this method is not called
    protected override void ProcessRecord()
    {
        try
        {
            if (HostAppBuilder.AppHost is null)
            {
                var msg = "AppHost is null";
                Console.WriteLine(msg);
                Log.Information(msg);

                try
                {
                    HostAppBuilder.BuildAppHost();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                    throw;
                }
            }
            else
            {
                var m = "AppHost is NOT null";
                Console.WriteLine(m);
                Log.Information(m);
            }

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var configFolder = Path.Combine(userProfile, ".nugetWorkflow");

            var todayFilename = Path.Combine(configFolder, $"top-packages-{DateTimeOffset.UtcNow:yyyy-MM-dd}.json");

            if (Force || !File.Exists(todayFilename))
            {
                List<PackageListing> results = new();

                foreach (var result in NugetOrg.GetPackages(NugetOrg.GetHtmlForPackages(), IncludeDetails))
                {
                    WriteObject(result);
                    results.Add(result);
                }

                var json = JsonConvert.SerializeObject(results);

                if (!Directory.Exists(configFolder))
                {
                    Directory.CreateDirectory(configFolder);
                }

                File.WriteAllText(todayFilename, json);
            }
            else
            {
                var json = File.ReadAllText(todayFilename);

                var results = JsonConvert.DeserializeObject<PackageListing[]>(json);

                WriteObject(results);
            }
        }
        catch(Exception ex)
        {
            Log.Error(ex,"");
        }
    }

    // This method will be called once at the end of pipeline execution; if no input is received, this method is not called
    protected override void EndProcessing()
    {
        NugetOrg.UpdateStatus -= NugetOrg_UpdateStatus;
        if (IsVerbose) WriteVerbose("Complete.");
        WriteInformation(new InformationRecord($"Complete.", GetType().Name));
    }
}
