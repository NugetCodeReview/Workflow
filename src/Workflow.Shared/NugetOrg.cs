using Newtonsoft.Json;

using Polly;
using Polly.RateLimit;

using System.Globalization;

namespace Workflow;

internal class NugetOrg
{
    const string BASE_URL = "https://www.nuget.org";
    const string URL = $"{BASE_URL}/stats/packages";
    const string ROWS_XPATH = "/html/body/div[2]/section/div[2]/div/table[2]/tbody/tr";

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

            return html;
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

            Task.Run(async () =>
            {
                html = await GetHtml(new Uri(URL));
                mre.Set();
            });

            mre.Wait(CancellationToken.None);

            return html;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception parsing html.");
            throw;
        }
    }
    internal static IEnumerable<PackageListing> GetPackages(
        string html,
        bool getDetails = false)
    {
        if(UpdateStatus is null)
        {
            Console.WriteLine($"{nameof(UpdateStatus)} is null");
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var rows = doc.DocumentNode.SelectNodes(ROWS_XPATH);

        var counter = 0;
        foreach (var row in rows)
        {
            var innerHtml = row.InnerHtml;

            PackageListing? parsed = ParseInnerHtml(innerHtml);

            if (parsed is not null)
            {
                try
                {
                    if (getDetails)
                    {
                        parsed.GetDetails(BASE_URL);
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

    private static PackageListing? ParseInnerHtml(string innerHtml)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(innerHtml);

        var nodes = doc.DocumentNode.ChildNodes.Where(
            n => n.NodeType == HtmlNodeType.Element).ToArray();

        if (int.TryParse(nodes[0].InnerText, out int rank))
        {
            return new(rank,
                nodes[1].ChildNodes.First().InnerText,
                nodes[1].ChildNodes.First().Attributes["href"].Value);
        }

        return default;
    }
}
