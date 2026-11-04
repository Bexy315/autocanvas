using AutoCanvas.Persistence.Identity;
using AutoCanvas.Persistence.Triggers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Repositories;

internal sealed class RepositoryWebhookConfiguration : IEntityTypeConfiguration<RepositoryWebhook>
{
    public void Configure(EntityTypeBuilder<RepositoryWebhook> builder)
    {
        builder.ToTable("repository_webhooks");
        builder.HasKey(w => w.RepositoryId);
        builder.Property(w => w.TokenHash).HasMaxLength(AutomationWebhook.HashLength);
        builder.HasIndex(w => w.TokenHash).IsUnique();
        builder.Property(w => w.Hint).HasMaxLength(AutomationWebhook.MaxHintLength);
        builder.Property(w => w.SecretCiphertext).HasMaxLength(AutomationWebhook.MaxSecretLength);
        builder.Property(w => w.CreatedByName).HasMaxLength(User.MaxUserNameLength);
        builder.HasOne<Repository>().WithMany().HasForeignKey(w => w.RepositoryId).OnDelete(DeleteBehavior.Cascade);
    }
}
