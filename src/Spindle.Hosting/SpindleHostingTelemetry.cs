using System.Diagnostics;

namespace Spindle.Hosting;

public class SpindleHostingTelemetry
{
    public const string ActivitySourceName = "Spindle.Hosting";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
