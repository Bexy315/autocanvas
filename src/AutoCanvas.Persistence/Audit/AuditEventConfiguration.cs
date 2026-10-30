using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Audit;

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public const int ActorNameLength = 200;
    public const int ActionLength = 100;
    public const int TargetTypeLength = 50;
    public const int TargetIdLength = 100;
    public const int ClientAddressLength = 64;
    public const int UserAgentLength = 500;

    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.ActorKind).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.ActorName).HasMaxLength(ActorNameLength);
        builder.Property(e => e.Action).HasMaxLength(ActionLength);
        builder.Property(e => e.TargetType).HasMaxLength(TargetTypeLength);
        builder.Property(e => e.TargetId).HasMaxLength(TargetIdLength);
        builder.Property(e => e.ClientAddress).HasMaxLength(ClientAddressLength);
        builder.Property(e => e.UserAgent).HasMaxLength(UserAgentLength);

        // Queries: newest first, per object, per folder subtree, per actor.
        builder.HasIndex(e => e.OccurredAt);
        builder.HasIndex(e => new { e.TargetType, e.TargetId });
        builder.HasIndex(e => e.FolderId);
        builder.HasIndex(e => e.ActorId);
    }
}
