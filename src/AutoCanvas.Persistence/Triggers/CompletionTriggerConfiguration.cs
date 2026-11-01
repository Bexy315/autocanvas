using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Triggers;

internal sealed class CompletionTriggerConfiguration : IEntityTypeConfiguration<CompletionTrigger>
{
    public void Configure(EntityTypeBuilder<CompletionTrigger> builder)
    {
        builder.ToTable("completion_triggers");
        builder.HasKey(c => c.AutomationId);
        builder.Property(c => c.TargetName).HasMaxLength(Automations.Automation.MaxNameLength);
        builder.Property(c => c.On).HasConversion<string>().HasMaxLength(20);

        // What a finished run looks for: the automations that follow its automation.
        builder.HasIndex(c => new { c.TargetFolderId, c.TargetName });
        builder.HasOne(c => c.Automation).WithMany().HasForeignKey(c => c.AutomationId).OnDelete(DeleteBehavior.Cascade);
    }
}
