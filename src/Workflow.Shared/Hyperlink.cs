namespace Workflow;

public record Hyperlink(string Text, string Url)
{
    public override string ToString() => $"{Text}: {Url}";
}