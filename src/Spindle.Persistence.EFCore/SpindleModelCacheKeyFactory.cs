using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Spindle.Persistence.EFCore;

internal sealed class SpindleModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context)
    {
        return Create(context, designTime: false);
    }

    public object Create(DbContext context, bool designTime)
    {
        return (
            context.GetType(),
            context.Database.ProviderName,
            context is SpindleDbContext spindleContext ? spindleContext.Schema : null,
            designTime);
    }
}
