//using System.Runtime.Remoting.Messaging;
using Newtonsoft.Json;

using System.Collections.Concurrent;

namespace Workflow;

[JsonDictionary]
public class TopPackages: ConcurrentDictionary<int, PackageListing>, IAsJson
{
    public PackageListing AddOrUpdatePackage(PackageListing package)
    {
        return AddOrUpdate(package.Rank, package, (_,_)=>package);
    }

    public TopPackages? FromJson<TopPackages>(string json)
    {
        return JsonConvert.DeserializeObject<TopPackages>(json, JsonManager.JsonSettings);
    }

    public IOrderedEnumerable<PackageListing> GetOrderedPackages()
    {
        return Values.OrderBy(x => x.Rank);
    }

    public string ToJson()
    {
        return JsonConvert.SerializeObject(this, JsonManager.JsonSettings );
    }
}