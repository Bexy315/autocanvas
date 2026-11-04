using AutoCanvas.Persistence.Identity;
using AutoCanvas.Persistence.Variables;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Repositories;

internal sealed class RepositoryConfiguration : IEntityTypeConfiguration<Repository>
{
    public void Configure(EntityTypeBuilder<Repository> builder)
    {
        builder.ToTable("repositories");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Name).HasMaxLength(Repository.MaxNameLength);
        builder.Property(r => r.NormalizedName).HasMaxLength(Repository.MaxNameLength);
        builder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Url).HasMaxLength(Repository.MaxUrlLength);
        builder.Property(r => r.DefaultBranch).HasMaxLength(Repository.MaxBranchLength);
        builder.Property(r => r.ResolvedBranch).HasMaxLength(Repository.MaxBranchLength);
        builder.Property(r => r.UserName).HasMaxLength(Repository.MaxUserNameLength);
        builder.Property(r => r.CredentialKeyId).HasMaxLength(Secret.MaxKeyIdLength);
        builder.Property(r => r.LastCommit).HasMaxLength(Repository.MaxCommitLength);
        builder.Property(r => r.LastError).HasMaxLength(Repository.MaxErrorLength);
        builder.Property(r => r.CreatedByName).HasMaxLength(User.MaxUserNameLength);
        builder.HasIndex(r => new { r.FolderId, r.NormalizedName }).IsUnique();

        // Restrict: a folder with connected repositories is not deleted (the use case refuses it).
        builder.HasOne(r => r.Folder).WithMany().HasForeignKey(r => r.FolderId).OnDelete(DeleteBehavior.Restrict);
    }
}
