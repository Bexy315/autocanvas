using AutoCanvas.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Runners;

internal sealed class RunnerConfiguration : IEntityTypeConfiguration<Runner>
{
    public void Configure(EntityTypeBuilder<Runner> builder)
    {
        builder.ToTable("runners");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Name).HasMaxLength(Runner.MaxNameLength);
        builder.Property(r => r.NormalizedName).HasMaxLength(Runner.MaxNameLength);
        builder.HasIndex(r => r.NormalizedName).IsUnique();
        builder.Property(r => r.CredentialHash).HasMaxLength(Session.TokenHashLength).IsFixedLength();
        builder.HasIndex(r => r.CredentialHash).IsUnique();
        builder.Property(r => r.CredentialHint).HasMaxLength(Runner.MaxHintLength);
        builder.Property(r => r.Capabilities).HasMaxLength(Runner.MaxCapabilitiesLength);
        builder.Property(r => r.CreatedByName).HasMaxLength(Runner.MaxCreatedByLength);
        builder.Property(r => r.Version).IsConcurrencyToken();
    }
}
