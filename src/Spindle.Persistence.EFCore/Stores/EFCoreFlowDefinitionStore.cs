using Microsoft.EntityFrameworkCore;
using Spindle.Abstractions.Core;
using Spindle.Persistence.EFCore.Entities;
using Spindle.Persistence.FlowDefinitions;
using System.Linq.Expressions;

namespace Spindle.Persistence.EFCore.Stores;

internal sealed class EFCoreFlowDefinitionStore(SpindleDbContext context) : IFlowDefinitionStore
{

    public async ValueTask UpsertAsync(
        FlowDefinitionRecord definition,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existing = await context.FlowDefinitions.FirstOrDefaultAsync(x =>
            x.FlowName == definition.FlowName.Value && x.FlowVersion == definition.FlowVersion.Value,
            cancellationToken: cancellationToken);

        if (existing == null)
        {
            await context.FlowDefinitions.AddAsync(new Entities.FlowDefinitionEntity
            {
                FlowName = definition.FlowName.Value,
                FlowVersion = definition.FlowVersion.Value,
                DefinitionHash = definition.DefinitionHash,
                FlowTypeName = definition.FlowTypeName,
                Definition = definition.Definition,
                CreatedAt = definition.CreatedAt,
                UpdatedAt = definition.UpdatedAt
            }, cancellationToken);
        }
        else
        {
            // It already exists, so we perform an update
            existing.DefinitionHash = definition.DefinitionHash;
            existing.FlowTypeName = definition.FlowTypeName;
            existing.Definition = definition.Definition;
            existing.UpdatedAt = definition.UpdatedAt;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask<FlowDefinitionRecord?> GetAsync(
        FlowName flowName,
        FlowVersion flowVersion,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await context.FlowDefinitions
            .AsNoTracking()
            .Where(x => x.FlowName == flowName.Value && x.FlowVersion == flowVersion.Value)
            .Select(Translation)
            .FirstOrDefaultAsync(cancellationToken: cancellationToken);
    }

    public async ValueTask<IReadOnlyList<FlowDefinitionRecord>> GetByNameAsync(
        FlowName flowName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await context.FlowDefinitions
            .AsNoTracking()
            .Where(x => x.FlowName == flowName.Value)
            .OrderBy(x => x.FlowVersion)
            .Select(Translation)
            .ToListAsync(cancellationToken: cancellationToken);
    }

    private readonly static Expression<Func<FlowDefinitionEntity, FlowDefinitionRecord>> Translation = x => new FlowDefinitionRecord
    {
        CreatedAt = x.CreatedAt,
        DefinitionHash = x.DefinitionHash,
        FlowName = new FlowName(x.FlowName),
        FlowTypeName = x.FlowTypeName,
        FlowVersion = new FlowVersion(x.FlowVersion),
        UpdatedAt = x.UpdatedAt,
        Definition = x.Definition
    };
}
