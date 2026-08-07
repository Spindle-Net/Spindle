using Spindle.Abstractions.Core;
using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Snapshot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Spindle.Runtime.Tests;

/// <summary>
/// Tests that test the <see cref="IFlowContext"/> step waiting methods
/// </summary>
public class WaitingTests : TestBase
{

    [Fact]
    public async Task WaitAll_SuspendsUntilAllStepsComplete()
    {
        var (runtime, store, _) = CreateRuntime();
        var flowName = new FlowName("waitall-suspends");
        var options = new StartFlowOptions { IdempotencyKey = "waitall-suspends" };

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                var a = context.Step<int>("a", "A", () => ValueTask.FromResult(1));
                var b = context.Step<int>("b", "B", () => ValueTask.FromResult(2));

                await context.WaitAll(a, b);

                return new TestResult(await a + await b);
            });

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        var waitingInstance = await store.FlowInstances.GetAsync(handle.InstanceId);
        var waitingSteps = await store.Steps.GetByFlowInstanceAsync(handle.InstanceId);

        Assert.NotNull(waitingInstance);
        Assert.Equal(FlowInstanceStatus.Waiting, waitingInstance.Status);
        Assert.All(waitingSteps, step => Assert.Equal(StepStatus.Ready, step.Status));

        var result = await runtime.RunAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        var completedInstance = await store.FlowInstances.GetAsync(handle.InstanceId);

        Assert.Equal(new TestResult(3), result);
        Assert.NotNull(completedInstance);
        Assert.Equal(FlowInstanceStatus.Completed, completedInstance.Status);
    }

    // TODO: Add throw test

}
