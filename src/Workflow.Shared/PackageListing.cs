//using System.Runtime.Remoting.Messaging;

using Workflow.Shared;

namespace Workflow;
public record PackageListing(int Rank, string? PackageName, string? PackageListingUrl) : IAsJson
{
    public PackageListing() : this(-1, null, null) { }

    [JsonIgnore]
    private IConfiguration _configuration;
    private Hyperlink? repository;

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
    public IConfiguration Configuration => _configuration ??=
        HostAppBuilder.AppHost.Services.GetRequiredService<IConfiguration>();

    const string ownersNodesXpath = "/html/body/div[2]/section/div/aside/div[3]/ul/li";
    const string anchorsXpath = "/html/body/div[2]/section/div/aside/div[2]/ul/li/a";
    private const string NUGET_CODE_REVIEW = "NugetCodeReview";
    private const string GIT = ".git";
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

            var listing = NugetOrg.GetHtml(uri).Result;

            if (listing is null)
            {
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
            if (anchorNodes is { Count: > 0 })
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

    internal Task<string> DownloadPackageAsync(string baseUrl)
    {
        if (Package is not null)
        {
            return NugetOrg.GetPackage(new(new Uri(baseUrl), Package.Url), PackageName!);
        }

        return Task.FromResult(string.Empty);
    }

    internal async Task<string?> ForkPackageRepositoryAsync()
    {
        if (Repository is null)
        {
            Log.Warning($"{PackageName} Repository.Url: {(Repository?.Url ?? "<<null>>")}");
            return null;
        }

        Log.Debug($"Attempting to fork {PackageName}.");

        var segments = Repository.Url.Split('/');

        var user = segments.Skip(3).First();
        var project = segments.Skip(4).First();
        if (project.EndsWith(GIT, StringComparison.OrdinalIgnoreCase))
        {
            project = project[0..^(GIT.Length)];
        }
        var forkName = $"{user}_{project}";

        GitHubClient github = new (new ProductHeaderValue(project));
        //var org = await github.User.Get(user);

        try
        {
            var tokenAuth = new Credentials(Configuration["GITHUB_TOKEN"]); // NOTE: not real token
            github.Credentials = tokenAuth;

            Log.Debug($"Loading forks for {user}/{project}.");

            var repository = github.Repository;

            Log.Debug($"Logging into {NUGET_CODE_REVIEW}.");

            GitHubClient client = LogIn(user, project, tokenAuth, NUGET_CODE_REVIEW, project, github);

            try
            {
                var contents = await client?.Repository.Content.GetAllContents(NUGET_CODE_REVIEW, forkName);
                var found = contents.Any();

                if (found)
                {
                    var baseAddress = client.Connection.BaseAddress.ToString();
                    Uri.TryCreate(new Uri(baseAddress),
                        $"{NUGET_CODE_REVIEW}/{forkName}.git",
                        out Uri uri);

                    ForkUrl = new Hyperlink($"{user}/{forkName}", uri!.ToString());
                    Log.Debug($"Fork exists for {PackageName} at {ForkUrl.Url}");
                    return ForkUrl.Url;
                }
            }
            catch { }

            try
            {
                Log.Debug($"Logging into {NUGET_CODE_REVIEW}.");

                var found = (await client?.Repository.Content.GetAllContents(NUGET_CODE_REVIEW, forkName)).Any();

                Repository repo = await RenameFork(user, project, forkName, client);

                return ForkUrl?.Url;
            }
            catch
            {
                Log.Information($"Forking {user}/{project} to {NUGET_CODE_REVIEW}/{project}");

                Repository? fork = await repository
                                    .Forks
                                    .Create(
                                        user,
                                        project,
                                        new NewRepositoryFork
                                        {
                                            Organization = NUGET_CODE_REVIEW
                                        });

                Log.Debug($"Logging into {NUGET_CODE_REVIEW}.");

                client = LogIn(user, project, tokenAuth, NUGET_CODE_REVIEW, project, github);

                Repository repo = await RenameFork(user, project, forkName, client);

                Log.Information($"Forked {PackageName} to {repo.Url}");

                return ForkUrl?.Url;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"While forking {Repository.Url}");
            //throw;
        }

        return default;

        async Task<Repository> RenameFork(
            string user, string project, string forkName, GitHubClient? client)
        {
            Log.Debug($"Renaming {PackageName} to {forkName}");

            var repo = await client.Repository
                                   .Edit(NUGET_CODE_REVIEW,
                                        project,
                                        new RepositoryUpdate(project)
                                        {
                                            Name = forkName,
                                        });

            ForkUrl = new Hyperlink($"{user}/{forkName}", repo.Url);
            return repo;
        }
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

    internal Task AddWorkflow() => this.AddToForkAsync();

    internal Task<string> DownloadSymbolsPackageAsync(string baseUrl)
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
    public Hyperlink? Repository
    {
        get => repository;
        private set
        {
            if (repository == value || value is null) return;

            if (!(value.Url?.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                value = value with { Url = value.Url + ".git" };
            }
            repository = value;
        }
    }
    [JsonProperty]
    public Hyperlink? Package { get; private set; }
    [JsonProperty]
    public Hyperlink? SymbolsPackage { get; private set; }
    [JsonRequired]
    public bool IsWhitelisted { get; private set; }
    [JsonProperty]
    public Hyperlink? ForkUrl { get; private set; }
    [JsonProperty]
    public List<ContentItem> Workflows { get; internal set; } = new();
    [JsonProperty]
    public List<ContentItem> Scripts { get; internal set; } = new();
    [JsonProperty]
    public string PagesUrl { get; internal set; }
}
