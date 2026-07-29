using Microsoft.EntityFrameworkCore;
using Spindle.Persistence.EFCore.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Spindle.Persistence.EFCore;

internal class SpindleDbContext : DbContext
{

    internal DbSet<ExecutionHistoryEntity> ExecutionHistories => Set<ExecutionHistoryEntity>();
    internal DbSet<FlowDefinitionEntity> FlowDefinitions => Set<FlowDefinitionEntity>();
    internal DbSet<FlowInstanceEntity> FlowInstances => Set<FlowInstanceEntity>();
    internal DbSet<InboxMessageEntity> InboxMessages => Set<InboxMessageEntity>();
    internal DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();
    internal DbSet<SignalEntity> Signals => Set<SignalEntity>();
    internal DbSet<SignalWaitEntity> SignalWaits => Set<SignalWaitEntity>();
    internal DbSet<StepAttemptEntity> StepAttempts => Set<StepAttemptEntity>();
    internal DbSet<StepInstanceEntity> StepInstances => Set<StepInstanceEntity>();
    internal DbSet<StepLeaseEntity> StepLeases => Set<StepLeaseEntity>();
    internal DbSet<TimerEntity> Timers => Set<TimerEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ExecutionHistoryEntity>()
            .ComplexProperty(x => x.Payload);

        modelBuilder.Entity<FlowDefinitionEntity>()
            .ComplexProperty(x => x.Definition);

        modelBuilder.Entity<FlowInstanceEntity>(e =>
        {
            e.ComplexProperty(x => x.Input);
            e.ComplexProperty(x => x.Result);
            e.HasOne(x => x.FlowDefinition)
                .WithMany(y => y.Instances)
                .HasForeignKey(x => new { x.FlowName, x.FlowVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InboxMessageEntity>()
            .ComplexProperty(x => x.Payload);

        modelBuilder.Entity<OutboxMessageEntity>(e =>
        {
            e.ComplexProperty(x => x.Payload);
            e.Property(x => x.Headers).HasJsonPropertyName("Headers");
        });

        modelBuilder.Entity<SignalEntity>()
            .ComplexProperty(x => x.Payload);

        modelBuilder.Entity<SignalWaitEntity>()
            .HasOne(x => x.Step)
            .WithMany(x => x.SignalWaits)
            .HasForeignKey(x => new { x.FlowInstanceId, x.StepId });

        modelBuilder.Entity<StepAttemptEntity>()
            .HasOne(x => x.Step)
            .WithMany(x => x.Attempts)
            .HasForeignKey(x => new { x.FlowInstanceId, x.StepId });

        modelBuilder.Entity<StepInstanceEntity>(e =>
        {
            e.ComplexProperty(x => x.Input);
            e.ComplexProperty(x => x.Result);
            e.Property(x => x.Dependencies).HasJsonPropertyName("Dependencies");
        });

        modelBuilder.Entity<StepLeaseEntity>()
            .HasOne(x => x.Step)
            .WithMany(x => x.Leases)   
            .HasForeignKey(x => new { x.FlowInstanceId, x.StepId });

        modelBuilder.Entity<TimerEntity>()
            .HasOne(x => x.Step)
            .WithMany(x => x.Timers)
            .HasForeignKey(x => new { x.FlowInstanceId, x.StepId });
    }

}
