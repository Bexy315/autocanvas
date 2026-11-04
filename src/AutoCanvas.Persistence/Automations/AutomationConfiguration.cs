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

        // No foreign key: adding one to an existing table rebuilds it on SQLite, outside a transaction.
        // Disconnecting a repository deletes its automations through the use case (ADR-0021).
        builder.Property(a => a.SourcePath).HasMaxLength(Repositories.Repository.MaxUrlLength);
        builder.HasIndex(a => a.RepositoryId);
    }
}
