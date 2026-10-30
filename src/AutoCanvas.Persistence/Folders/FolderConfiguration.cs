using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Folders;

internal sealed class FolderConfiguration : IEntityTypeConfiguration<Folder>
{
    public void Configure(EntityTypeBuilder<Folder> builder)
    {
        builder.ToTable("folders");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();
        builder.Property(f => f.Name).HasMaxLength(Folder.MaxNameLength);
        builder.Property(f => f.NormalizedName).HasMaxLength(Folder.MaxNameLength);
        builder.Property(f => f.Description).HasMaxLength(Folder.MaxDescriptionLength);
        builder.Property(f => f.Path).HasMaxLength(Folder.MaxPathLength);
        builder.Property(f => f.Version).IsConcurrencyToken();

        builder.HasIndex(f => new { f.ParentKey, f.NormalizedName }).IsUnique();
        builder.HasIndex(f => f.Path);

        // Restrict: a folder with subfolders is not deleted by accident (the use case refuses it).
        builder.HasOne(f => f.Parent).WithMany().HasForeignKey(f => f.ParentId).OnDelete(DeleteBehavior.Restrict);
    }
}
