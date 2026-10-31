using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Automations;

internal sealed class AutomationConfiguration : IEntityTypeConfiguration<Automation>
{
    public void Configure(EntityTypeBuilder<Automation> builder)
    {
        builder.ToTable("automations");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Name).HasMaxLength(Automation.MaxNameLength);
        builder.Property(a => a.NormalizedName).HasMaxLength(Automation.MaxNameLength);
        builder.Property(a => a.Title).HasMaxLength(Automation.MaxTitleLength);
        builder.Property(a => a.Description).HasMaxLength(Automation.MaxDescriptionLength);
        builder.Property(a => a.CurrentVersion).IsConcurrencyToken();
        builder.HasIndex(a => new { a.FolderId, a.NormalizedName }).IsUnique();

        // Restrict: a folder that still holds automations is not deleted (the use case refuses it).
        builder.HasOne(a => a.Folder).WithMany().HasForeignKey(a => a.FolderId).OnDelete(DeleteBehavior.Restrict);
    }
}
