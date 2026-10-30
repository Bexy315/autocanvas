using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Folders;

internal sealed class FolderGrantConfiguration : IEntityTypeConfiguration<FolderGrant>
{
    public void Configure(EntityTypeBuilder<FolderGrant> builder)
    {
        // Plain SQL that PostgreSQL and SQLite both accept (ADR-0010).
        builder.ToTable("folder_grants", table => table.HasCheckConstraint(
            "ck_folder_grants_one_subject",
            "(user_id IS NULL) <> (group_id IS NULL)"));
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).ValueGeneratedNever();
        builder.Property(g => g.Role).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(g => new { g.FolderId, g.UserId }).IsUnique();
        builder.HasIndex(g => new { g.FolderId, g.GroupId }).IsUnique();
        builder.HasIndex(g => g.UserId);
        builder.HasIndex(g => g.GroupId);

        builder.HasOne(g => g.Folder).WithMany().HasForeignKey(g => g.FolderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(g => g.User).WithMany().HasForeignKey(g => g.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(g => g.Group).WithMany().HasForeignKey(g => g.GroupId).OnDelete(DeleteBehavior.Cascade);
    }
}
