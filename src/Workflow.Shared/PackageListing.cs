//using System.Runtime.Remoting.Messaging;

using System.Web;

using Workflow.Shared;

namespace Workflow;

public record PackageListing(
    int Rank,
    string? PackageName,
    string? PackageListingUrl,
    DateTimeOffset? AsOf = default) : IAsJson
{
    public PackageListing() : this(-1, null, null) { }

    [JsonIgnore]
    private IConfiguration? _configuration;
    private Hyperlink? repository;
    private static PackagesConfig _config;

    static PackageListing()
    {
        //Console.WriteLine($"static PackageListing(): Entered.");
        try
        {
            _expressions = new();
            foreach (var pattern in Config.Whitelist ?? Array.Empty<string>())
            {
                var regex = new Regex(pattern, RegexOptions.Singleline);
                _expressions.Add(regex);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            throw;
        }
        finally
        {
            //Console.WriteLine($"static PackageListing(): Finalize.");
        }
    }

    [JsonIgnore]
    public PackageHeader PackageHeader
    {
        get; init;
    }

    [JsonIgnore]
    public string SafePackageName => HttpUtility.UrlEncode(PackageName)!;

    [JsonIgnore]
    public IConfiguration Configuration => _configuration ??=
        HostAppBuilder.AppHost.Services.GetRequiredService<IConfiguration>();

    const string ownersNodesXpath = "/html/body/div[2]/section/div/aside/div[3]/ul/li";
    const string ownersNodesXpathAlt = "/html/body/div[3]/section/div/aside/div[3]/ul/li";
    const string anchorsXpath = "/html/body/div[2]/section/div/aside/div[2]/ul/li/a";
    const string anchorsXpathAlt = "/html/body/div[3]/section/div/aside/div[2]/ul/li/a";
    private static readonly List<Regex> _expressions;

    public override string ToString()
        => $"{Rank}: {PackageName} - {OwnerNames}";

    [JsonIgnore]
    private static PackagesConfig Config
        => _config ??= HostAppBuilder.AppHost.Services.GetRequiredService<PackagesConfig>();

    internal Task<string> GetDetails(string baseUrl)
    {
        if (PackageListingUrl is null or "")
        {
            throw new ArgumentNullException(nameof(PackageListingUrl));
        }

        try
        {
            Uri uri = new(new Uri(baseUrl), PackageListingUrl);

            var listing = NugetOrg.GetHtml(uri).Result;
            //Log.Information($"listing: {listing}");

            if (listing is null)
            {
                return Task.FromResult("");
            }

            var packageDirectory =
                Path.Combine(Config.DownloadFolder!, JsonManager.TodayString, PackageName!);

            if (!Directory.Exists(packageDirectory))
            {
                Directory.CreateDirectory(packageDirectory);
            }

            var htmlFileName = Path.Combine(packageDirectory, $"{SafePackageName}.html");

            File.WriteAllText(htmlFileName, listing, System.Text.Encoding.UTF8);

            var doc = new HtmlDocument();
            doc.LoadHtml(listing);

            var ownersNodes = doc.DocumentNode.SelectNodes(ownersNodesXpath)?.ToArray();
            ownersNodes ??= doc.DocumentNode.SelectNodes(ownersNodesXpathAlt)?.ToArray();

            if (ownersNodes is not null)
            {
                Owners = ParseList(ownersNodes).ToList();

                if (_expressions is not null)
                {
                    foreach (var regex in _expressions)
                    {
                        foreach (var owner in Owners)
                        {
                            if (owner?.Text is { Length: > 0 } &&
                                regex.IsMatch(owner.Text))
                            {
                                PackageHeader.IsWhitelisted = true;
                                return Task.FromResult("");
                            }
                        }
                    }
                }
                else
                {
                    Log.Error("_expressions is null");
                }
            }
            else
            {
                Log.Error("ownersNodes is null");
            }

            var anchorNodes = doc.DocumentNode.SelectNodes(anchorsXpath)
                ?? doc.DocumentNode.SelectNodes(anchorsXpathAlt);

            if (anchorNodes is { Count: > 0 })
            {
                foreach (var anchorNode in anchorNodes)
                {
                    var parsedAnchor = ParseAnchor(anchorNode);

                    switch (parsedAnchor?.Text)
                    {
                        case "Source repository":
                            PackageHeader.Repository = parsedAnchor;
                            break;

                        case "Download package":
                            Package = parsedAnchor;
                            DownloadPackageAsync(baseUrl);
                            break;

                        case "Download symbols":
                            SymbolsPackage = parsedAnchor;
                            DownloadSymbolsPackageAsync(baseUrl);
                            break;
                    }
                }
            }
            else
            {
                Log.Error("anchorNodes is null or empty");
            }

            var versionRows = doc.DocumentNode.SelectNodes("//*[@id=\"versions-tab\"]/div/table/tbody/tr");

            PackageHeader.PackageVersions.Clear();
            foreach (var versionRow in versionRows)
            {
                var columns = versionRow.ChildNodes.Where(c => c.NodeType==HtmlNodeType.Element).ToArray();
                var version = columns[0].ChildNodes.First(c => c.NodeType==HtmlNodeType.Element).GetDirectInnerText().Trim();
                var downloads = columns[1].GetDirectInnerText().Trim();
                var datePublished = columns[2].ChildNodes.First(c => c.NodeType == HtmlNodeType.Element).GetDirectInnerText().Trim();

                var versionInfo = new VersionInfo(this, version, downloads, datePublished);

                PackageHeader.PackageVersions.Add(versionInfo);
            }

            return PackageHeader.SaveAsync(packageDirectory);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"{MethodBase.GetCurrentMethod()?.Name}");

            return Task.FromException<string>(ex);
        }
        finally
        {
            //Log.Information($"GetDetails(string baseUrl): {Rank} - {PackageName}: Finally");
        }
    }

    internal Task<string> DownloadPackageAsync(string baseUrl)
    {
        if (Package is not null)
        {
            return NugetOrg.GetPackage(new(new Uri(baseUrl), Package.Url), this);
        }

        return Task.FromResult(string.Empty);
    }

    public static GitHubClient LogIn(
        string user,
        string project,
        Credentials tokenAuth,
        string org,
        string newName,
        GitHubClient github)
            => new GitHubClient(
                new Connection(
                    new ProductHeaderValue(newName),
                    github.Connection.BaseAddress))
            {
                Credentials = tokenAuth
            };

    internal Task<string> DownloadSymbolsPackageAsync(string baseUrl)
    {
        if (SymbolsPackage is not null)
        {
            return NugetOrg.GetPackage
                (new(new Uri(baseUrl), SymbolsPackage.Url),
                this);
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
        foreach (var owner in ownersNodes)
        {
            if (owner.NodeType == HtmlNodeType.Element)
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
    public Hyperlink? Package { get; private set; }
    [JsonProperty]
    public Hyperlink? SymbolsPackage { get; private set; }

    [JsonProperty]
    public DateTimeOffset DateTaken { get; set; } = DateTimeOffset.UtcNow;
}
