using Newtonsoft.Json;

using Polly;
using Polly.RateLimit;

using System.Globalization;

using Workflow.Shared;

namespace Workflow;

internal static class NugetOrg
{
    const string BASE_URL = "https://www.nuget.org";
    const string URL = $"{BASE_URL}/stats/packages";
    const string ROWS_XPATH = "/html/body/div[2]/section/div[2]/div/table[2]/tbody/tr";
    const string ALT_ROWS_XPATH = "/html/body/div[3]/section/div[2]/div/table[2]/tbody/tr";

    private static PackagesConfig Config { get; set; }
    static NugetOrg()
    {
        //Console.WriteLine($"static PackageListing(): Entered.");
        try
        {
            Config ??= HostAppBuilder.AppHost.Services.GetRequiredService<PackagesConfig>();
            if (Config is null)
            {
                Config = new PackagesConfig();
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


    internal static async Task<string> GetHtml(Uri query)
    {
        //Log.Debug($"Getting html from {query}");
        var waitFor = TimeSpan.FromMilliseconds(
            new Random((int)DateTime.Now.Ticks).Next(250, 1000));

        await Task.Delay(waitFor);

        var httpClient = new HttpClient();

        var rateLimit = Policy.RateLimitAsync(20, TimeSpan.FromSeconds(1), 10);

        try
        {
            var html = await rateLimit.ExecuteAsync(() => httpClient.GetStringAsync(query));

            httpClient.Dispose();

            var page = new HtmlDocument();
            var baseNode = page.CreateElement("base");
            baseNode.SetAttributeValue("href", $"{query.GetLeftPart(UriPartial.Authority)}");
            baseNode.SetAttributeValue("target", "_blank");

            page.LoadHtml(html);
            var parent = page.DocumentNode
                .ChildNodes
                .First(n => n.NodeType == HtmlNodeType.Element)
                .ChildNodes
                .First(n => n.NodeType == HtmlNodeType.Element);

            parent.InnerHtml = baseNode.OuterHtml + parent.InnerHtml;

            var packageDirectory = Path.Combine(Config.DownloadFolder!, JsonManager.TodayString);

            if (!Directory.Exists(packageDirectory))
            {
                Directory.CreateDirectory(packageDirectory);
            }

            var htmlFileName = Path.Combine(packageDirectory, "top-packages.html");

            page.Save(htmlFileName);

            page.Load(htmlFileName);

            return page.DocumentNode.OuterHtml;
        }
        catch (HttpRequestException hqe)
        {
            var delay = TimeSpan.FromMilliseconds(
                new Random((int)DateTime.Now.Ticks).Next(1000, 3000));

            return await Retry(query, httpClient, hqe, delay);
        }
        catch (RateLimitRejectedException ex)
        {
            return await Retry(query, httpClient, ex, ex.RetryAfter);
        }

        static async Task<string> Retry(
            Uri query,
            HttpClient httpClient,
            Exception ex,
            TimeSpan delay)
        {
            //Log.Error(ex, $"{ex.Message}, delaying {delay} then retying.");
            httpClient.Dispose();
            await Task.Delay(delay);

            return await GetHtml(query);
        }
    }

    internal static async Task<string> GetPackage(Uri query, PackageListing package)
    {
        string packageName = package.PackageName!;
        string safePackageName = package.SafePackageName!;

        //Log.Debug($"Getting html from {query}");
        var waitFor = TimeSpan.FromMilliseconds(
            new Random((int)DateTime.Now.Ticks).Next(250, 1000));

        await Task.Delay(waitFor);

        var httpClient = new HttpClient();

        var rateLimit = Policy.RateLimitAsync(20, TimeSpan.FromSeconds(1), 10);

        try
        {
            var bytes = await rateLimit.ExecuteAsync(() => httpClient.GetByteArrayAsync(query));

            httpClient.Dispose();

            var packageDirectory = Path.Combine(Config.DownloadFolder!, JsonManager.TodayString);

            if (!Directory.Exists(packageDirectory))
            {
                Directory.CreateDirectory(packageDirectory);
            }

            packageDirectory = Path.Combine(packageDirectory, packageName);

            var version = query.PathAndQuery.Split("/".ToCharArray()).Last();
            var symbolsSection = query.PathAndQuery.IndexOf("symbolpackage", StringComparison.OrdinalIgnoreCase) > -1 ? "symbols." : "";
            var filename = $"{packageName}.{version}.{symbolsSection}nupkg";
            var packageFileName = Path.Combine(packageDirectory, filename);

            await File.WriteAllBytesAsync(packageFileName, bytes);

            return packageFileName;
        }
        catch (HttpRequestException hqe)
        {
            var delay = TimeSpan.FromMilliseconds(
                new Random((int)DateTime.Now.Ticks).Next(1000, 3000));

            return await Retry(query, httpClient, hqe, delay);
        }
        catch (RateLimitRejectedException ex)
        {
            return await Retry(query, httpClient, ex, ex.RetryAfter);
        }

        static async Task<string> Retry(
            Uri query,
            HttpClient httpClient,
            Exception ex,
            TimeSpan delay)
        {
            //Log.Error(ex, $"{ex.Message}, delaying {delay} then retying.");
            httpClient.Dispose();
            await Task.Delay(delay);

            return await GetHtml(query);
        }
    }

    internal static string GetHtmlForPackages()
    {
        try
        {
            ManualResetEventSlim mre = new(false);
            string html = "";
            Uri uri = new Uri(URL);

            Task.Run(async () =>
            {
                html = await GetHtml(uri);
                mre.Set();
            });

            mre.Wait(CancellationToken.None);

            var page = new HtmlDocument();
            page.LoadHtml(html);

            return page.DocumentNode.OuterHtml;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception parsing html.");
            throw;
        }
    }

    internal static IEnumerable<PackageHeader> GetPackages(
        string html,
        bool getDetails = false)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var rows = doc.DocumentNode.SelectNodes(ROWS_XPATH);

        rows ??= doc.DocumentNode.SelectNodes(ALT_ROWS_XPATH);

        var counter = 0;
        foreach (var row in rows)
        {
            var innerHtml = row.InnerHtml;

            (int rank, PackageHeader? parsed) = ParseInnerHtml(innerHtml);

            if (parsed is not null)
            {
                try
                {
                    if (getDetails)
                    {
                        var result = parsed.GetDetails(BASE_URL, rank).Result;
                    }
                }
                catch(Exception ex)
                {
                    Console.Error.WriteLine(ex);
                }

                yield return parsed;
            }

            InvokeUpdateStatus(counter++, rows.Count, parsed?.ToString() ?? "null");
        }
    }

    private static void InvokeUpdateStatus(int current, int itemCount, string status)
    {
        //Log.Information($"{current} of {itemCount}: {status} {UpdateStatus is null}");
        UpdateStatus?.Invoke(current, itemCount, status);
    }

    public static event Action<int, int, string>? UpdateStatus;

    private static (int rank, PackageHeader? header) ParseInnerHtml(string innerHtml)
    {
        //Log.Information($"innerHtml: {innerHtml}");
        var doc = new HtmlDocument();
        doc.LoadHtml(innerHtml);

        var nodes = doc.DocumentNode.ChildNodes.Where(
            n => n.NodeType == HtmlNodeType.Element).ToArray();

        //Log.Information($"nodes.Length: {nodes.Length}");

        if (int.TryParse(nodes[0].InnerText, out int rank))
        {
            return (rank, new(
                nodes[1].ChildNodes.First().InnerText,
                nodes[1].ChildNodes.First().Attributes["href"].Value));
        }

        return default;
    }
}
