using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Repositories;

internal sealed class RepositoryPullRequestConfiguration : IEntityTypeConfiguration<RepositoryPullRequest>
{
    public void Configure(EntityTypeBuilder<RepositoryPullRequest> builder)
    {
        builder.ToTable("repository_pull_requests");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Action).HasMaxLength(RepositoryPullRequest.MaxActionLength);
        builder.Property(p => p.HeadSha).HasMaxLength(Repository.MaxCommitLength);
        builder.Property(p => p.HeadBranch).HasMaxLength(RepositoryRef.MaxRefLength);
        builder.Property(p => p.BaseBranch).HasMaxLength(RepositoryRef.MaxRefLength);
        builder.Property(p => p.Title).HasMaxLength(RepositoryPullRequest.MaxTitleLength);
        builder.Property(p => p.Author).HasMaxLength(RepositoryPush.MaxPusherLength);
        builder.Property(p => p.Url).HasMaxLength(Repository.MaxUrlLength);
        builder.HasIndex(p => p.ReceivedAt);
        builder.HasOne<Repository>().WithMany().HasForeignKey(p => p.RepositoryId).OnDelete(DeleteBehavior.Cascade);
    }
}
