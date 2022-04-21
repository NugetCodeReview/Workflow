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

    [System.Management.Automation.Parameter(
        Mandatory = false,
        Position = 0,
        ValueFromPipeline = false,
        ValueFromPipelineByPropertyName = false)]
    public SwitchParameter IncludeDetails { get; set; }

    [System.Management.Automation.Parameter(
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
        Console.Write($"{(counter % 10 == 0 ? "*" : ".")}");
    }

    // This method will be called for each input received from the pipeline to this cmdlet; if no input is received, this method is not called
    protected override async void ProcessRecord()
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

            var config = HostAppBuilder.AppHost!.Services.GetRequiredService<PackagesConfig>();

            if (Force || !JsonManager.TodayFile.Exists)
            {
                Log.Debug("Pulling Live Data.");

                List<PackageListing> results = new();

                var html = NugetOrg.GetHtmlForPackages();

                Log.Information($"html length: {html.Length}");

                foreach (var result in NugetOrg.GetPackages(
                    html, IncludeDetails))
                {
                    WriteObject(result);
                    results.Add(result);
                    Console.Write($"{(results.Count%10==0?"+":"-")}");
                }

                if (!config.ConfigDirectoryInfo.Exists)
                {
                    config.ConfigDirectoryInfo.Create();
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

                var results = JsonManager.TodayFile
                                         .FullName
                                         .DeserializeObject<List<PackageListing>>();

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
