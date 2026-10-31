using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Runs;

internal sealed class RunConfiguration : IEntityTypeConfiguration<Run>
{
    public void Configure(EntityTypeBuilder<Run> builder)
    {
        builder.ToTable("runs");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.AutomationName).HasMaxLength(Automations.Automation.MaxNameLength);
        builder.Property(r => r.State).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Trigger).HasConversion<string>().HasMaxLength(30);
        builder.Property(r => r.TriggeredByName).HasMaxLength(Run.MaxTriggeredByNameLength);
        builder.Property(r => r.CancelledByName).HasMaxLength(Run.MaxTriggeredByNameLength);
        builder.HasIndex(r => new { r.AutomationId, r.Number }).IsUnique();
        builder.HasIndex(r => new { r.FolderId, r.CreatedAt });
        builder.HasIndex(r => r.State);

        // The run outlives its automation; deleting the folder (only possible once its automations
        // are gone) removes the runs that are left in it.
        builder.HasOne(r => r.Automation).WithMany().HasForeignKey(r => r.AutomationId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(r => r.Folder).WithMany().HasForeignKey(r => r.FolderId).OnDelete(DeleteBehavior.Cascade);
    }
}
