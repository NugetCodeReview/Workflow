using Newtonsoft.Json;

namespace Workflow;

public record Hyperlink(string? Text, string? Url) : IAsJson
{
    public Hyperlink() : this(null, null) { }

    public override string ToString() => $"{Text}: {Url}";

    public string ToJson()
    {
        return JsonConvert.SerializeObject(this, JsonManager.JsonSettings);
    }

    public Hyperlink? FromJson<Hyperlink>(string json)
    {
        return JsonConvert.DeserializeObject<Hyperlink>(json, JsonManager.JsonSettings);
    }
}