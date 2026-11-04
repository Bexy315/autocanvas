using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Repositories;

internal sealed class RepositoryRefConfiguration : IEntityTypeConfiguration<RepositoryRef>
{
    public void Configure(EntityTypeBuilder<RepositoryRef> builder)
    {
        builder.ToTable("repository_refs");
        builder.HasKey(r => new { r.RepositoryId, r.Ref });
        builder.Property(r => r.Ref).HasMaxLength(RepositoryRef.MaxRefLength);
        builder.Property(r => r.Commit).HasMaxLength(Repository.MaxCommitLength);

        // The refs go with their repository.
        builder.HasOne<Repository>().WithMany().HasForeignKey(r => r.RepositoryId).OnDelete(DeleteBehavior.Cascade);
    }
}
