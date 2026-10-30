using AutoCanvas.Persistence.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Identity;

internal sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("sessions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.TokenHash).HasMaxLength(Session.TokenHashLength).IsFixedLength();
        builder.HasIndex(s => s.TokenHash).IsUnique();
        builder.Property(s => s.ClientAddress).HasMaxLength(AuditEvent.MaxClientAddressLength);
        builder.Property(s => s.UserAgent).HasMaxLength(AuditEvent.MaxUserAgentLength);
        builder.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
