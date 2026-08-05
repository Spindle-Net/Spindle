using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Spindle.Persistence.EFCore;

public class SpindleEFCoreTelemetry
{

    public const string ActivitySourceName = "Spindle.Persistence.EFCore";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName);

}
