using AutoCanvas.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Runners;

internal sealed class RunnerRegistrationTokenConfiguration : IEntityTypeConfiguration<RunnerRegistrationToken>
{
    public void Configure(EntityTypeBuilder<RunnerRegistrationToken> builder)
    {
        builder.ToTable("runner_registration_tokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Description).HasMaxLength(RunnerRegistrationToken.MaxDescriptionLength);
        builder.Property(t => t.TokenHash).HasMaxLength(Session.TokenHashLength).IsFixedLength();
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.Property(t => t.Hint).HasMaxLength(Runner.MaxHintLength);
        builder.Property(t => t.CreatedByName).HasMaxLength(Runner.MaxCreatedByLength);

        // Of two registrations with the same token, only the first can mark it used.
        builder.Property(t => t.UsedAt).IsConcurrencyToken();
        builder.HasOne<Runner>().WithMany().HasForeignKey(t => t.RunnerId).OnDelete(DeleteBehavior.SetNull);
    }
}
