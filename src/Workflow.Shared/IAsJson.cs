//using System.Runtime.Remoting.Messaging;
namespace Workflow
{
    internal interface IAsJson
    {
        string ToJson();
        TObject? FromJson<TObject>(string json);
    }
}