using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Triggers;

internal sealed class AutomationWebhookConfiguration : IEntityTypeConfiguration<AutomationWebhook>
{
    public void Configure(EntityTypeBuilder<AutomationWebhook> builder)
    {
        builder.ToTable("automation_webhooks");
        builder.HasKey(w => w.AutomationId);
        builder.Property(w => w.TokenHash).HasMaxLength(AutomationWebhook.HashLength);
        builder.HasIndex(w => w.TokenHash).IsUnique();
        builder.Property(w => w.Hint).HasMaxLength(AutomationWebhook.MaxHintLength);
        builder.Property(w => w.SigningSecret).HasMaxLength(AutomationWebhook.MaxSecretLength);
        builder.Property(w => w.CreatedByName).HasMaxLength(200);
        builder.HasOne(w => w.Automation).WithMany().HasForeignKey(w => w.AutomationId).OnDelete(DeleteBehavior.Cascade);
    }
}
