//using System.Runtime.Remoting.Messaging;
using AzurePipelinesToGitHubActionsConverter.Core;

using System.Net.Http.Json;

using Workflow.Shared;

namespace Workflow;

public static class WorkflowBuilder
{
    private const string NUGET_CODE_REVIEW = "NugetCodeReview";
    private const string GIT = ".git";
    private const string CODE_REVIEW_YAML_PATH = ".github/workflows/code-review.yml";
    private const string CODE_REVIEW_DOCS = "docs";

    [JsonIgnore]
    private static PackagesConfig Config
        => HostAppBuilder.AppHost.Services.GetRequiredService<PackagesConfig>();

    internal static async Task AddToForkAsync(this PackageHeader packageHeader)
    {
        if (packageHeader.ForkUrl is null || packageHeader.Repository is null)
        {
            Log.Debug($"AddToForkAsync - ForkUrl: {(packageHeader?.ForkUrl?.Url ?? "<<null>>")}, " +
                $"Repository: {(packageHeader?.Repository?.Url ?? "<<null>>")}");
            return;
        }

        try
        {
            string url = packageHeader.ForkUrl.Url!;

            (string user, string project) = GetUserProjectFromUrl(url);

            (GitHubClient github, GitHubClient client) = GetClients(user, project);

            var allContents = await GetContents(client, user, project);

            var wrong = allContents.SingleOrDefault(i => i.RepoContent.Path == ".github/workflow/code-review.yml");

            if(wrong is not null)
            {
                await client.Repository.Content.DeleteFile(user, project,
                    ".github/workflow/code-review.yml",
                    new DeleteFileRequest("Replacing", wrong.RepoContent.Sha));
            }

            var regex = new Regex(@"[/\\]?.github[/\\]workflows", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
            var workflows = allContents.Where(ac => regex.IsMatch(ac.Name)).ToList();

            if (!workflows.Any(w => w.Name.Equals("code-review.yml")))
            {
                await CreateWorkflow();
            }

            packageHeader.Workflows = workflows;

            regex = new Regex(@"\b.*\.ps[m]?1\b", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
            var scripts = allContents.Where(ac => regex.IsMatch(ac.Name)).ToList();

            packageHeader.Scripts = scripts;
            return;

            async Task CreateWorkflow(bool force = false, string contentsYaml = "# Code Review")
            {
                var azurePipelineYaml = allContents.FirstOrDefault(i
                    => i.Name.Equals("Azure-pipelines.yml", StringComparison.OrdinalIgnoreCase));

                if (force || azurePipelineYaml is null)
                {
                    try
                    {
                        var existing = allContents.SingleOrDefault(i => i.Name == CODE_REVIEW_YAML_PATH);

                        if (existing is not null)
                        {
                            await client.Repository.Content.DeleteFile(user, project,
                                CODE_REVIEW_YAML_PATH,
                                new DeleteFileRequest("Replacing", existing.RepoContent.Sha));
                        }

                        var newWorkflow = await client.Repository.Content.CreateFile(
                            user,
                            project,
                            CODE_REVIEW_YAML_PATH,
                            new CreateFileRequest(
                                "Added stub for code-review script.",
                                contentsYaml));
                        var contents = await client.Repository.Content.GetAllContents(user, project);
                        allContents = await GetContentItems(client, user, project, contents, null);
                        workflows = allContents.Where(ac => regex.IsMatch(ac.Name)).ToList();
                    }
                    catch(Exception ex)
                    {
                        Log.Error(ex, $"force: {force}");
                    }
                }
                else
                {
                    try
                    {
                        IApiResponse<RepositoryContent> content = await client.Connection.Post<RepositoryContent>(
                            new Uri(azurePipelineYaml.RepoContent.Url));
                        var yaml = content.Body.Content;
                        Conversion conversion = new();
                        ConversionResponse gitHubOutput = conversion.ConvertAzurePipelineToGitHubAction(yaml);
                        var actionsYaml = gitHubOutput.actionsYaml;
                        await CreateWorkflow(true, actionsYaml);
                    }
                    catch(Exception ex)
                    {
                        Log.Error(ex, $"azurePipelineYaml.RepoContent.Url: {azurePipelineYaml.RepoContent.Url}");
                        //Log.Error(ex, $"azurePipelineYaml.RepoContent.GitUrl: {azurePipelineYaml.RepoContent.GitUrl}");
                        //Log.Error(ex, $"azurePipelineYaml.RepoContent.HtmlUrl: {azurePipelineYaml.RepoContent.HtmlUrl}");
                        //Log.Error(ex, $"azurePipelineYaml.RepoContent.DownloadUrl: {azurePipelineYaml.RepoContent.DownloadUrl}");
                    }
                }
            }

        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Package: {packageHeader.PackageName}, {packageHeader.ForkUrl?.Url}");
        }
    }

    internal static async Task CreateDocs(this PackageHeader packageHeader)
    {
        if (packageHeader.ForkUrl is null || packageHeader.Repository is null)
        {
            Log.Debug($"CreateDocs - ForkUrl: {(packageHeader?.ForkUrl?.Url ?? "<<null>>")}, " +
                $"Repository: {(packageHeader?.Repository?.Url ?? "<<null>>")}");
            return;
        }

        try
        {
            string url = packageHeader.ForkUrl.Url!;

            (var user, var project) = GetUserProjectFromUrl(url);

            (var github, var client) = GetClients(user, project);

            var allContents = await GetContents(client, user, project);

            var docsFolder = allContents.SingleOrDefault(i => i.Name == CODE_REVIEW_DOCS);

            if (docsFolder is null)
            {
                var newWorkflow = await client.Repository.Content.CreateFile(
                    user,
                    project,
                    $"{CODE_REVIEW_DOCS}/index.md",
                    new CreateFileRequest(
                        "Added stub for docs.",
                        $"# Code Review Results for {packageHeader.PackageName}"));
                var contents = await client.Repository.Content.GetAllContents(user, project);
            }

            var repository = await client.Repository.Get(user, project);

            if (!repository.HasPages)
            {
                using var httpClient = HostAppBuilder.AppHost.Services.GetRequiredService<HttpClient>();
                var path = ApiUrls.RepositoryPage(repository.Id).ToString();
                var result = await httpClient.PostAsync(
                    new Uri(client.Connection.BaseAddress, path),
                    JsonContent.Create(new
                    {
                        owner = user,
                        repo = project,
                        source = new
                        {
                            branch = repository.DefaultBranch,
                            path = $"/{CODE_REVIEW_DOCS}"
                        }
                    }));

                if (result.IsSuccessStatusCode)
                {
                    repository = await client.Repository.Get(user, project);

                    var pages = await client.Repository.Page.Get(repository.Id);
                    packageHeader.PagesUrl = pages.HtmlUrl;
                }
            }

            return;
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Package: {packageHeader.PackageName}, {packageHeader.ForkUrl?.Url}");
        }
    }

    private static async Task<List<ContentItem>> GetContents(
    GitHubClient client, string user, string project)
    {
        try
        {
            Log.Debug($"Getting contents of {user}/{project}");
            IReadOnlyList<RepositoryContent>? contents =
                await client.Repository.Content.GetAllContents(user, project);

            List<ContentItem> allContents =
                await GetContentItems(client, user, project, contents, null);

            return allContents;
        }
        catch(RateLimitExceededException rlee)
        {
            Log.Error($"Rate Limit Exceeded: {JsonConvert.SerializeObject(rlee)}");
            throw;
        }
    }

    private static (GitHubClient github, GitHubClient client) GetClients(string user, string project)
    {
        GitHubClient github = new(new ProductHeaderValue(project));

        var tokenAuth = new Credentials(Config.GITHUB_TOKEN); // NOTE: not real token
        github.Credentials = tokenAuth;

        var repository = github.Repository;

        GitHubClient client = PackageListing.LogIn(
            user, project, tokenAuth, NUGET_CODE_REVIEW, project, github);

        return (github, client);
    }

    private static (string user, string project) GetUserProjectFromUrl(string url)
    {
        url = url.Replace("https://api.github.com/repos", "https://github.com");

        var segments = url.Split('/');

        var user = segments.Skip(3).First();
        var project = segments.Skip(4).First();
        if (project.EndsWith(GIT, StringComparison.OrdinalIgnoreCase))
        {
            project = project[0..^(GIT.Length)];
        }

        return (user, project);
    }

    private static async Task<List<ContentItem>> GetContentItems(
        GitHubClient client,
        string user, string project,
        IReadOnlyList<RepositoryContent> contents,
        ContentItem? parent = null)
    {
        List<ContentItem> items = new();
        foreach (RepositoryContent item in contents
            .OrderBy(i => i.Type == "File" ? 0 : 1)
            .ThenBy(i => i.Name))
        {
            item.Type.TryParse(out var contentType);

            ContentItem? contentItem = contentType switch
            {
                ContentType.Dir => new(item.Path, contentType, item.DownloadUrl, parent, item),
                ContentType.File => new(item.Path, contentType, item.DownloadUrl, parent, item),
                _ => default
            };

            if (contentItem is not null)
            {
                items.Add(contentItem);

                if (contentItem.Type is ContentType.Dir)
                {
                    var children = await client.Repository.Content.GetAllContents(
                        user, project, contentItem.Name);

                    items.AddRange(await GetContentItems(client, user, project, children, contentItem));
                }
            }
        }

        return items;
    }

}
