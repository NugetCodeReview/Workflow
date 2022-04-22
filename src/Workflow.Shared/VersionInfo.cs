//using System.Runtime.Remoting.Messaging;

namespace Workflow;

public record VersionInfo(
    PackageListing PackageListing,
    string Version,
    string Downloads,
    string DatePublished);