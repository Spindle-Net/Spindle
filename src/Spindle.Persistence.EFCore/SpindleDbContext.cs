using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Spindle.Abstractions.Snapshot;
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var payloadConverter = new ValueConverter<SerializedPayload, string>(
            payload => JsonSerializer.Serialize(payload, JsonSerializerOptions.Default),
            json => JsonSerializer.Deserialize<SerializedPayload>(json, JsonSerializerOptions.Default)!);
        var payloadComparer = new ValueComparer<SerializedPayload>(
            (left, right) =>
                left != null && right != null &&
                left.ContentType == right.ContentType &&
                left.TypeName == right.TypeName &&
                left.Data.SequenceEqual(right.Data),
            payload => HashCode.Combine(
                payload.ContentType,
                payload.TypeName,
                payload.Data.Length),
            payload => new SerializedPayload
            {
                ContentType = payload.ContentType,
                TypeName = payload.TypeName,
                Data = payload.Data.ToArray()
            });
        var stringListConverter = new ValueConverter<IReadOnlyList<string>, string>(
            values => JsonSerializer.Serialize(values, JsonSerializerOptions.Default),
            json => JsonSerializer.Deserialize<List<string>>(json, JsonSerializerOptions.Default)!);
        var stringListComparer = new ValueComparer<IReadOnlyList<string>>(
            (left, right) => left != null && right != null && left.SequenceEqual(right),
            values => values.Aggregate(0, HashCode.Combine),
            values => values.ToArray());
        var headersConverter = new ValueConverter<IReadOnlyDictionary<string, string>, string>(
            values => JsonSerializer.Serialize(values, JsonSerializerOptions.Default),
            json => JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonSerializerOptions.Default)!);
        var headersComparer = new ValueComparer<IReadOnlyDictionary<string, string>>(
            (left, right) => DictionariesEqual(left, right),
            values => values.OrderBy(pair => pair.Key).Aggregate(
                0,
                (hash, pair) => HashCode.Combine(hash, pair.Key, pair.Value)),
            values => new Dictionary<string, string>(values));
        var dateTimeOffsetConverter = new ValueConverter<DateTimeOffset, long>(
            value => value.UtcTicks,
            value => new DateTimeOffset(value, TimeSpan.Zero));

        ConfigurePayload(modelBuilder.Entity<ExecutionHistoryEntity>().Property(entity => entity.Payload));
        ConfigurePayload(modelBuilder.Entity<FlowDefinitionEntity>().Property(entity => entity.Definition));
        ConfigurePayload(modelBuilder.Entity<FlowInstanceEntity>().Property(entity => entity.Input));
        ConfigurePayload(modelBuilder.Entity<FlowInstanceEntity>().Property(entity => entity.Result));
        ConfigurePayload(modelBuilder.Entity<InboxMessageEntity>().Property(entity => entity.Payload));
        ConfigurePayload(modelBuilder.Entity<OutboxMessageEntity>().Property(entity => entity.Payload));
        ConfigurePayload(modelBuilder.Entity<SignalEntity>().Property(entity => entity.Payload));
        ConfigurePayload(modelBuilder.Entity<StepInstanceEntity>().Property(entity => entity.Input));
        ConfigurePayload(modelBuilder.Entity<StepInstanceEntity>().Property(entity => entity.Result));

        modelBuilder.Entity<StepInstanceEntity>()
            .Property(entity => entity.Dependencies)
            .HasConversion(stringListConverter, stringListComparer);
        modelBuilder.Entity<OutboxMessageEntity>()
            .Property(entity => entity.Headers)
            .HasConversion(headersConverter, headersComparer);

        modelBuilder.Entity<FlowInstanceEntity>()
            .HasIndex(entity => new { entity.FlowName, entity.IdempotencyKey })
            .IsUnique();
        modelBuilder.Entity<FlowInstanceEntity>().Property(entity => entity.FlowName).HasMaxLength(255);
        modelBuilder.Entity<FlowInstanceEntity>().Property(entity => entity.FlowVersion).HasMaxLength(255);
        modelBuilder.Entity<FlowInstanceEntity>().Property(entity => entity.IdempotencyKey).HasMaxLength(255);
        modelBuilder.Entity<FlowInstanceEntity>()
            .HasIndex(entity => new { entity.Status, entity.UpdatedAt });
        modelBuilder.Entity<StepInstanceEntity>()
            .HasIndex(entity => new { entity.Status, entity.CreatedAt });
        modelBuilder.Entity<TimerEntity>()
            .HasIndex(entity => new { entity.FiredAt, entity.DueAt });
        modelBuilder.Entity<SignalWaitEntity>()
            .HasIndex(entity => new { entity.SignalName, entity.CorrelationKey, entity.CompletedAt });
        modelBuilder.Entity<SignalWaitEntity>().Property(entity => entity.SignalName).HasMaxLength(255);
        modelBuilder.Entity<SignalWaitEntity>().Property(entity => entity.CorrelationKey).HasMaxLength(255);
        modelBuilder.Entity<OutboxMessageEntity>()
            .HasIndex(entity => new { entity.PublishedAt, entity.CreatedAt });

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset) ||
                    property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(dateTimeOffsetConverter);
                }

                if (property.ClrType == typeof(string) &&
                    (property.IsPrimaryKey() || property.IsForeignKey()))
                {
                    property.SetMaxLength(255);
                }
            }
        }

        void ConfigurePayload<TProperty>(
            Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<TProperty> property)
        {
            property.HasConversion(payloadConverter, payloadComparer);
        }
    }

    private static bool DictionariesEqual(
        IReadOnlyDictionary<string, string>? left,
        IReadOnlyDictionary<string, string>? right)
    {
        return left != null &&
            right != null &&
            left.Count == right.Count &&
            left.All(pair =>
                right.TryGetValue(pair.Key, out var value) &&
                value == pair.Value);
    }
}
