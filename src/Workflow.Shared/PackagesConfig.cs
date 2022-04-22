public class PackagesConfig
{
    public string[]? Whitelist { get; set; }
    public string[]? Blacklist { get; set; }
    public string? DownloadFolder { get; set; }
    public string? GitRoot { get; set; }

    public string? ConfigFolder { get; set; }

    public DirectoryInfo ConfigDirectoryInfo => new (ConfigFolder);

    public string? GITHUB_TOKEN { get; set; }
}
