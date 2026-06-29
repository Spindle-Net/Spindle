using System.Security.Cryptography;
using System.Text;
using Spindle.Abstractions.Core;
using Spindle.Abstractions.Flows;

namespace Spindle;

public sealed class FlowDescriptor
{
    internal FlowDescriptor(
        FlowName flowName,
        FlowVersion flowVersion,
        Type flowType,
        Type requestType,
        Type resultType,
        Func<IFlowContext, object?, ValueTask<object?>> execute)
    {
        FlowName = flowName;
        FlowVersion = flowVersion;
        FlowType = flowType;
        RequestType = requestType;
        ResultType = resultType;
        Execute = execute;
        DefinitionHash = CreateDefinitionHash(flowName, flowVersion, flowType, requestType, resultType);
    }

    public FlowName FlowName { get; }

    public FlowVersion FlowVersion { get; }

    public Type FlowType { get; }

    public Type RequestType { get; }

    public Type ResultType { get; }

    public string DefinitionHash { get; }

    internal Func<IFlowContext, object?, ValueTask<object?>> Execute { get; }

    private static string CreateDefinitionHash(
        FlowName flowName,
        FlowVersion flowVersion,
        Type flowType,
        Type requestType,
        Type resultType)
    {
        var input = string.Join(
            "|",
            flowName.Value,
            flowVersion.Value,
            flowType.AssemblyQualifiedName,
            requestType.AssemblyQualifiedName,
            resultType.AssemblyQualifiedName);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }
}
