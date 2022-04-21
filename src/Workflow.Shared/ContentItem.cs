//using System.Runtime.Remoting.Messaging;

namespace Workflow;

public record ContentItem(string? Name, ContentType? Type, string? DownloadUrl, ContentItem? Parent)
{
    [JsonConstructor]
    ContentItem()
        : this(null, default, null, null) { }

    public ContentItem(string? Name, ContentType? Type, string? DownloadUrl, ContentItem? Parent, RepositoryContent repoContent) :
        this(Name, Type, DownloadUrl, Parent)
    {
        this.RepoContent = repoContent;
    }

    [JsonIgnore]
    public RepositoryContent? RepoContent { get; init; }
}