//using System.Runtime.Remoting.Messaging;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Workflow.Shared;

namespace Workflow;
public record PackageListing(int Rank, string PackageName, string PackageListingUrl)
{
    static PackageListing()
    {
        Console.WriteLine($"static PackageListing(): Entered.");
        try
        {
            _configuration = Options.Create(new PackagesConfig()).Value;
            var config = HostAppBuilder.AppHost.Services.GetService<IConfiguration>();

            if (config is not null)
            {
                config.GetSection("Packages").Bind(_configuration);

                _expressions = new();
                foreach (var pattern in _configuration.Whitelist ?? Array.Empty<string>())
                {
                    var regex = new Regex(pattern, RegexOptions.Singleline);
                    _expressions.Add(regex);
                }
            }
            else
            {
                _expressions = new List<Regex>();
            }
        }
        catch(Exception ex){
            Console.Error.WriteLine(ex);
            throw;
        }
        finally
        {
            Console.WriteLine($"static PackageListing(): Finalize.");
        }
    }

    const string ownersNodesXpath = "/html/body/div[2]/section/div/aside/div[3]/ul/li";
    const string sourceRepositoryXpath = "/html/body/div[2]/section/div/aside/div[2]/ul/li/a";
    const string downloadPackageXpath = "/html/body/div[2]/section/div/aside/div[2]/ul/li[5]/a";
    const string downloadSymbolsPackageXpath = "/html/body/div[2]/section/div/aside/div[2]/ul/li[6]/a";
    private static readonly PackagesConfig _configuration;
    private static readonly List<Regex> _expressions;

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

            var anchorNodes = doc.DocumentNode.SelectNodes(sourceRepositoryXpath);
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
                            break;

                        case "Download symbols":
                            SymbolsPackage = parsedAnchor;
                            break;
                    }
                }
            }
            else
            {
                Console.Error.WriteLine("anchorNodes is null or empty");
            }

            //var downloadPackageNode = doc.DocumentNode.SelectSingleNode(downloadPackageXpath);
            //if (downloadPackageNode is not null)
            //{
            //    Package = ParseAnchor(downloadPackageNode);
            //}
            //else
            //{
            //    Console.Error.WriteLine("downloadPackageNode is null");
            //}

            //var downloadSymbolsPackageNode = doc.DocumentNode.SelectSingleNode(downloadSymbolsPackageXpath);
            //if (downloadSymbolsPackageNode is not null)
            //{
            //    SymbolsPackage = ParseAnchor(downloadSymbolsPackageNode);
            //}
            //else
            //{
            //    Console.Error.WriteLine("downloadSymbolsPackageNode is null");
            //}
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

    public List<Hyperlink>? Owners { get; private set; }
    public string OwnerNames => string.Join(", ", Owners.Select(o => o.Text));
    public Hyperlink? Repository { get; private set; }
    public Hyperlink? Package { get; private set; }
    public Hyperlink? SymbolsPackage { get; private set; }
    public bool IsWhitelisted { get; private set; }
}
