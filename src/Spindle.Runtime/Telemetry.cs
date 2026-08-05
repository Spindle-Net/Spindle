using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Spindle.Runtime;

public class Telemetry
{

    public const string ActivitySourceName = "Spindle";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName);

}
