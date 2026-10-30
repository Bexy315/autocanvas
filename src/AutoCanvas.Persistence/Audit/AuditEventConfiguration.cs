using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Audit;

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.ActorKind).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.ActorName).HasMaxLength(AuditEvent.MaxActorNameLength);
        builder.Property(e => e.Action).HasMaxLength(AuditEvent.MaxActionLength);
        builder.Property(e => e.TargetType).HasMaxLength(AuditEvent.MaxTargetTypeLength);
        builder.Property(e => e.TargetId).HasMaxLength(AuditEvent.MaxTargetIdLength);
        builder.Property(e => e.ClientAddress).HasMaxLength(AuditEvent.MaxClientAddressLength);
        builder.Property(e => e.UserAgent).HasMaxLength(AuditEvent.MaxUserAgentLength);

        // Queries: newest first, per object, per folder subtree, per actor.
        builder.HasIndex(e => e.OccurredAt);
        builder.HasIndex(e => new { e.TargetType, e.TargetId });
        builder.HasIndex(e => e.FolderId);
        builder.HasIndex(e => e.ActorId);
    }
}
