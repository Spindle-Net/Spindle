using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Spindle.Abstractions.Snapshot;
using Spindle.Persistence.EFCore.Configuration;
using Spindle.Persistence.EFCore.Entities;

namespace Spindle.Persistence.EFCore;

public sealed class SpindleDbContext(
    DbContextOptions<SpindleDbContext> options)
    : DbContext(options)
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

    protected override void ConfigureConventions(
        ModelConfigurationBuilder configurationBuilder)
    {
        if (Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            configurationBuilder
                .Properties<DateTimeOffset>()
                .HaveConversion<DateTimeOffsetUtcConverter>();
        }
        else if (!UsesNativeDateTimeOffsetStorage())
        {
            configurationBuilder
                .Properties<DateTimeOffset>()
                .HaveConversion<DateTimeOffsetUtcTicksConverter>();
        }

        if (Database.ProviderName == "MySql.EntityFrameworkCore")
        {
            configurationBuilder
                .Properties<List<string>>()
                .HaveConversion<StringListJsonConverter, StringListValueComparer>();
        }

        configurationBuilder
            .Properties<IReadOnlyDictionary<string, string>>()
            .HaveConversion<StringDictionaryJsonConverter, StringDictionaryValueComparer>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ExecutionHistoryEntity>(entity =>
        {
            entity.ToTable("ExecutionHistories");
            entity.OwnsOne(
                owner => owner.Payload,
                payload => ConfigurePayload(payload, "Payload"));
        });

        modelBuilder.Entity<FlowDefinitionEntity>(entity =>
        {
            entity.ToTable("FlowDefinitions");
            entity.OwnsOne(
                owner => owner.Definition,
                payload => ConfigurePayload(payload, "Definition"));
        });

        modelBuilder.Entity<FlowInstanceEntity>(entity =>
        {
            entity.ToTable("FlowInstances");
            entity.ComplexProperty(
                owner => owner.Input,
                payload => ConfigurePayload(payload, "Input"));
            entity.OwnsOne(
                owner => owner.Result,
                payload => ConfigurePayload(payload, "Result"));
        });

        modelBuilder.Entity<InboxMessageEntity>(entity =>
        {
            entity.ToTable("InboxMessages");
            entity.ComplexProperty(
                owner => owner.Payload,
                payload => ConfigurePayload(payload, "Payload"));
        });

        modelBuilder.Entity<OutboxMessageEntity>(entity =>
        {
            entity.ToTable("OutboxMessages");
            entity.ComplexProperty(
                owner => owner.Payload,
                payload => ConfigurePayload(payload, "Payload"));

            var jsonColumnType = GetNativeJsonColumnType();
            if (jsonColumnType != null)
            {
                entity
                    .Property(owner => owner.Headers)
                    .HasColumnType(jsonColumnType);
            }
        });

        modelBuilder.Entity<SignalEntity>(entity =>
        {
            entity.ToTable("Signals");
            entity.OwnsOne(
                owner => owner.Payload,
                payload => ConfigurePayload(payload, "Payload"));
        });

        modelBuilder.Entity<SignalWaitEntity>(entity =>
            entity.ToTable("SignalWaits"));

        modelBuilder.Entity<StepAttemptEntity>(entity =>
            entity.ToTable("StepAttempts"));

        modelBuilder.Entity<StepDependencyEntity>(entity =>
        {
            entity.HasOne(x => x.Step)
                .WithMany(x => x.Dependencies)
                .HasForeignKey(x => new { x.FlowInstanceId, x.StepId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.DependsOn)
                .WithMany(x => x.Dependents)
                .HasForeignKey(x => new { x.FlowInstanceId, x.DependsOnId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StepInstanceEntity>(entity =>
        {
            entity.ToTable("StepInstances");

            entity.OwnsOne(
                owner => owner.Input,
                payload => ConfigurePayload(payload, "Input"));
            entity.OwnsOne(
                owner => owner.Result,
                payload => ConfigurePayload(payload, "Result"));
        });

        modelBuilder.Entity<StepLeaseEntity>(entity =>
            entity.ToTable("StepLeases"));

        modelBuilder.Entity<TimerEntity>(entity =>
            entity.ToTable("Timers"));
    }

    private static void ConfigurePayload<TOwner>(
        OwnedNavigationBuilder<TOwner, SerializedPayload> payload,
        string columnPrefix)
        where TOwner : class
    {
        payload
            .Property(value => value.ContentType)
            .HasColumnName($"{columnPrefix}_ContentType");

        payload
            .Property(value => value.TypeName)
            .HasColumnName($"{columnPrefix}_TypeName");

        payload
            .Property(value => value.Data)
            .HasColumnName($"{columnPrefix}_Data");
    }

    private static void ConfigurePayload(
        ComplexPropertyBuilder<SerializedPayload> payload,
        string columnPrefix)
    {
        payload
            .Property(value => value.ContentType)
            .HasColumnName($"{columnPrefix}_ContentType");

        payload
            .Property(value => value.TypeName)
            .HasColumnName($"{columnPrefix}_TypeName");

        payload
            .Property(value => value.Data)
            .HasColumnName($"{columnPrefix}_Data");
    }

    private string? GetNativeJsonColumnType()
    {
        return Database.ProviderName switch
        {
            "Npgsql.EntityFrameworkCore.PostgreSQL" => "jsonb",
            "MySql.EntityFrameworkCore" => "json",
            _ => null
        };
    }

    private bool UsesNativeDateTimeOffsetStorage()
    {
        return Database.ProviderName is
            "Microsoft.EntityFrameworkCore.SqlServer" or
            "Npgsql.EntityFrameworkCore.PostgreSQL";
    }
}
