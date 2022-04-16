//using System.Runtime.Remoting.Messaging;
using System.Collections.Concurrent;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Options;

using Newtonsoft.Json;

using Workflow.Shared;

namespace Workflow;
public record PackageListing(int Rank, string? PackageName, string? PackageListingUrl) : IAsJson
{
    public PackageListing() : this(-1, null, null) { }

    static PackageListing()
    {
        //Console.WriteLine($"static PackageListing(): Entered.");
        try
        {
            Config ??= HostAppBuilder.AppHost.Services.GetRequiredService<PackagesConfig>();
            if (Config is not null)
            {
                _expressions = new();
                foreach (var pattern in Config.Whitelist ?? Array.Empty<string>())
                {
                    var regex = new Regex(pattern, RegexOptions.Singleline);
                    _expressions.Add(regex);
                }
            }
            else
            {
                Config = new PackagesConfig();
                _expressions = new List<Regex>();
            }
        }
        catch(Exception ex){
            Console.Error.WriteLine(ex);
            throw;
        }
        finally
        {
            //Console.WriteLine($"static PackageListing(): Finalize.");
        }
    }

    const string ownersNodesXpath = "/html/body/div[2]/section/div/aside/div[3]/ul/li";
    const string anchorsXpath = "/html/body/div[2]/section/div/aside/div[2]/ul/li/a";
    private static readonly List<Regex> _expressions;

    public override string ToString()
        => $"{Rank}: {PackageName} - {OwnerNames}";

    [JsonIgnore]
    private static PackagesConfig Config { get; set; }


    internal void GetDetails(string baseUrl)
    {
        if (PackageListingUrl is null or "")
        {
            throw new ArgumentNullException(nameof(PackageListingUrl));
        }

        try
        {
            Uri uri = new(new Uri(baseUrl), PackageListingUrl);

            // Log.Information($"GetDetails(string baseUrl): {Rank} - {PackageName}: Start");

            var listing = NugetOrg.GetHtml(uri).Result;

            if (listing is null)
            {
                // Console.Error.WriteLine("listing is null");
                return;
            }

            var packageDirectory =
                Path.Combine(Config.DownloadFolder, JsonManager.TodayString, PackageName);

            if (!Directory.Exists(packageDirectory))
            {
                Directory.CreateDirectory(packageDirectory);
            }

            var htmlFileName = Path.Combine(packageDirectory, "package.html");

            File.WriteAllText(htmlFileName, listing, System.Text.Encoding.UTF8);

            var doc = new HtmlDocument();
            doc.LoadHtml(listing);

            var ownersNodes = doc.DocumentNode.SelectNodes(ownersNodesXpath).ToArray();
            Owners = ParseList(ownersNodes).ToList();

            if (_expressions is not null)
            {
                foreach (var regex in _expressions)
                {
                    foreach (var owner in Owners)
                    {
                        if (regex.IsMatch(owner.Text))
                        {
                            IsWhitelisted = true;
                            return;
                        }
                    }
                }
            }
            else
            {
                Console.Error.WriteLine("_expressions is null");
            }

            var anchorNodes = doc.DocumentNode.SelectNodes(anchorsXpath);
            if (anchorNodes is { Count: > 0})
            {
                foreach (var anchorNode in anchorNodes)
                {
                    //Log.Information($"anchorNode: {anchorNode.OuterHtml}");

                    var parsedAnchor = ParseAnchor(anchorNode);

                    //Log.Information($"parsedAnchor: {parsedAnchor?.Text ?? "<<null>>"}");

                    switch (parsedAnchor?.Text)
                    {
                        case "Source repository":
                            Repository = parsedAnchor;
                            break;

                        case "Download package":
                            Package = parsedAnchor;
                            DownloadPackage(baseUrl);
                            break;

                        case "Download symbols":
                            SymbolsPackage = parsedAnchor;
                            DownloadSymbolsPackage(baseUrl);
                            break;
                    }
                }
            }
            else
            {
                Console.Error.WriteLine("anchorNodes is null or empty");
            }

            var jsonFileName = Path.Combine(packageDirectory, "package.json");

            var json = ToJson();

            File.WriteAllText(jsonFileName, json, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
        }
        finally
        {
            //Log.Information($"GetDetails(string baseUrl): {Rank} - {PackageName}: Finally");
        }
    }

    internal Task<string> DownloadPackage(string baseUrl)
    {
        if (Package is not null)
        {
            return NugetOrg.GetPackage(new(new Uri(baseUrl), Package.Url), PackageName!);
        }

        return Task.FromResult(string.Empty);
    }

    internal Task<string> DownloadSymbolsPackage(string baseUrl)
    {
        if (SymbolsPackage is not null)
        {
            return NugetOrg.GetPackage(new(new Uri(baseUrl), SymbolsPackage.Url), PackageName!);
        }

        return Task.FromResult(string.Empty);
    }

    private Hyperlink? ParseAnchor(HtmlNode node)
    {
        if (node.NodeType == HtmlNodeType.Element)
        {
            return new Hyperlink(node.InnerText.Trim(), node.Attributes["href"]?.Value?.Trim() ?? "");
        }

        return null;
    }

    private IEnumerable<Hyperlink> ParseList(HtmlNode[] ownersNodes)
    {
        foreach(var owner in ownersNodes)
        {
            if(owner.NodeType == HtmlNodeType.Element)
            {
                var toParse = owner.ChildNodes.LastOrDefault(n => n.NodeType == HtmlNodeType.Element);
                if (toParse is not null)
                {
                    var parsed = ParseAnchor(toParse);

                    if (parsed is not null)
                    {
                        yield return parsed;
                    }
                }
            }
        }
    }

    public string ToJson()
    {
        return JsonConvert.SerializeObject(this, JsonManager.JsonSettings);
    }

    public PackageListing? FromJson<PackageListing>(string json)
    {
        return JsonConvert.DeserializeObject<PackageListing>(json, JsonManager.JsonSettings);
    }

    [JsonIgnore]
    public string OwnerNames => string.Join(", ", (Owners?.Select(o => o.Text) ?? new List<string>()));

    [JsonRequired]
    public List<Hyperlink>? Owners { get; private set; }
    [JsonProperty]
    public Hyperlink? Repository { get; private set; }
    [JsonProperty]
    public Hyperlink? Package { get; private set; }
    [JsonProperty]
    public Hyperlink? SymbolsPackage { get; private set; }
    [JsonRequired]
    public bool IsWhitelisted { get; private set; }
}
