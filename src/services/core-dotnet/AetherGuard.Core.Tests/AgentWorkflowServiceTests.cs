using System;
using System.Threading;
using System.Threading.Tasks;
using AetherGuard.Core.Data;
using AetherGuard.Core.Services;
using AetherGuard.Grpc.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AetherGuard.Core.Tests;

public class AgentWorkflowServiceTests
{
    [Fact]
    public async Task RegisterAsync_DisablesLocalInference_WhenRolloutDisabled()
    {
        await using var db = CreateDbContext();
        var service = CreateService(
            db,
            new AgentInferenceOptions
            {
                EnableLocalInferenceRollout = false,
                RolloutPercentage = 100
            });

        var result = await service.RegisterAsync(new RegisterRequest
        {
            Hostname = "agent-rollout-disabled"
        }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Payload);
        Assert.False(result.Payload!.Config.EnableLocalInference);
    }

    [Fact]
    public async Task RegisterAsync_EnablesLocalInference_WhenRolloutAt100Percent()
    {
        await using var db = CreateDbContext();
        var service = CreateService(
            db,
            new AgentInferenceOptions
            {
                EnableLocalInferenceRollout = true,
                RolloutPercentage = 100
            });

        var result = await service.RegisterAsync(new RegisterRequest
        {
            Hostname = "agent-rollout-100"
        }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Payload);
        Assert.True(result.Payload!.Config.EnableLocalInference);
    }

    private static AgentWorkflowService CreateService(
        ApplicationDbContext db,
        AgentInferenceOptions options)
    {
        return new AgentWorkflowService(
            db,
            NullLogger<AgentWorkflowService>.Instance,
            Options.Create(options));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"agent-workflow-tests-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(dbOptions);
    }
}
