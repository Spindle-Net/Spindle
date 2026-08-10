using Spindle.Abstractions.Core;
using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Nodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Spindle;

public static class FlowContextForkExtensions
{

    public static ForkNode<Unit> Fork(
        this IFlowContext ctx,
        string id,
        Func<IFlowContext, Task> descriptor)
        => ctx.Fork(id, async a =>
        {
            await descriptor(a);
            return Unit.Value;
        });

}
