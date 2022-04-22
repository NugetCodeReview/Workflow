//using System.Runtime.Remoting.Messaging;

using System.Web;

using Workflow.Shared;

namespace Workflow;

public record PackageHeader(string PackageName, string? PackageUrl) : IAsJson
{
    private const string NUGET_CODE_REVIEW = "NugetCodeReview";
    private const string GIT = ".git";
    public PackageHeader? FromJson<PackageHeader>(string json)
    {
        return JsonConvert.DeserializeObject<PackageHeader>(json, JsonManager.JsonSettings);
    }

    public string ToJson()
    {
        return JsonConvert.SerializeObject(this, JsonManager.JsonSettings);
    }

    internal async Task<string> SaveAsync(string packageDirectory)
    {
        var jsonFileName = Path.Combine(packageDirectory, $"{SafePackageName}.json");

        var json = ToJson();

        await File.WriteAllTextAsync(jsonFileName, json, Encoding.UTF8);

        return jsonFileName;
    }

    private PackageListing? _current;

    public async Task<string> GetDetails(string baseUrl, int rank)
    {
        if (_current?.DateTaken.Date != DateTimeOffset.UtcNow.Date) _current = null;
        _current ??= new PackageListing(rank, PackageName, PackageUrl) { PackageHeader = this };

        var result = await _current.GetDetails(baseUrl);

        return result;
    }

    internal async Task<string?> ForkPackageRepositoryAsync()
    {
        if (Repository is null)
        {
            Log.Warning($"{PackageName} Repository.Url: {(Repository?.Url ?? "<<null>>")}");
            return null;
        }

        Log.Debug($"Attempting to fork {PackageName}.");

        var segments = Repository.Url?.Split('/') ?? Array.Empty<string>();

        if (segments.Length >= 5)
        {
            var user = segments.Skip(3).First();
            var project = segments.Skip(4).First();
            if (project.EndsWith(GIT, StringComparison.OrdinalIgnoreCase))
            {
                project = project[0..^(GIT.Length)];
            }
            var forkName = $"{user}_{project}";

            GitHubClient github = new(new ProductHeaderValue(project));
            //var org = await github.User.Get(user);

            try
            {
                var tokenAuth = new Credentials(Config.GITHUB_TOKEN); // NOTE: not real token

                github.Credentials = tokenAuth;

                Log.Debug($"Loading forks for {user}/{project}.");

                var repository = github.Repository;

                Log.Debug($"Logging into {NUGET_CODE_REVIEW}.");

                GitHubClient client = LogIn(user, project, tokenAuth, NUGET_CODE_REVIEW, project, github);

                if (client is null)
                {
                    return default;
                }

                try
                {
                    var contents = await client.Repository.Content.GetAllContents(NUGET_CODE_REVIEW, forkName);
                    var found = contents.Any();

                    if (found)
                    {
                        var baseAddress = client.Connection.BaseAddress.ToString();
                        Uri.TryCreate(new Uri(baseAddress),
                            $"{NUGET_CODE_REVIEW}/{forkName}.git",
                            out Uri? uri);

                        if (uri is not null)
                        {
                            ForkUrl = new Hyperlink($"{user}/{forkName}", uri.ToString());
                            Log.Debug($"Fork exists for {PackageName} at {ForkUrl.Url}");
                            return ForkUrl.Url;
                        }
                    }
                }
                catch { }

                try
                {
                    Log.Debug($"Logging into {NUGET_CODE_REVIEW}.");

                    var found = (await client.Repository.Content.GetAllContents(NUGET_CODE_REVIEW, forkName)).Any();

                    Repository repo = await RenameFork(user, project, forkName, client);

                    return ForkUrl?.Url;
                }
                catch(Octokit.RateLimitExceededException rlee)
                {
                    Log.Error($"Rate Limit Exceeded: {JsonConvert.SerializeObject(rlee)}");
                    throw;
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
        }

        return default;

        async Task<Repository> RenameFork(
            string user, string project, string forkName, GitHubClient client)
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

    [JsonIgnore]
    private IConfiguration? _configuration;
    private Hyperlink? repository;
    private static List<PackageHeader>? _allHeaders;

    [JsonIgnore]
    public IConfiguration Configuration => _configuration ??=
        HostAppBuilder.AppHost.Services.GetRequiredService<IConfiguration>();

    [JsonIgnore]
    private static PackagesConfig Config
        => HostAppBuilder.AppHost.Services.GetRequiredService<PackagesConfig>();

    [JsonIgnore]
    public string SafePackageName => HttpUtility.UrlEncode(PackageName)!;

    [JsonRequired]
    public bool IsWhitelisted { get; internal set; }

    [JsonProperty]
    public Hyperlink? Repository
    {
        get => repository;
        internal set
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
    public Hyperlink? ForkUrl { get; internal set; }
    [JsonProperty]
    public string? PagesUrl { get; internal set; }

    [JsonProperty]
    public List<ContentItem> Workflows { get; internal set; } = new();
    [JsonProperty]
    public List<ContentItem> Scripts { get; internal set; } = new();
    [JsonRequired]
    public List<Hyperlink> Owners { get; internal set; } = new();
    [JsonProperty]
    public List<VersionInfo> PackageVersions { get; set; } = new();

    internal static List<PackageHeader> AllHeaders
    {
        get
        {
            if(_allHeaders is null or { Count: 0 })
            {
                if (JsonManager.TodayFile.Exists)
                {
                    using var readStream = JsonManager.TodayFile.OpenRead();
                    using var reader = new StreamReader(readStream);
                    var json = reader.ReadToEnd();

                    _allHeaders = JsonConvert
                        .DeserializeObject<List<PackageHeader>>(
                            json,
                            JsonManager.JsonSettings);
                }
                else
                {
                    _allHeaders = new();
                }
            }

            return _allHeaders!;
        }
    }

    public override string ToString()
        => $"{Current?.Rank.ToString() ?? "<<null>>"}: " +
        $"{PackageName} - {Current?.OwnerNames ?? "<<null>>"}";

    [JsonIgnore]
    private PackageListing? Current => PackageVersions.OrderByDescending(i => i.DatePublished).FirstOrDefault()?.PackageListing;

    [JsonIgnore]
    public string OwnerNames => string.Join(", ", (Owners?.Select(o => o.Text) ?? new List<string>()));

}
