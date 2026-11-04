using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Repositories;

internal sealed class PullRequestCommentConfiguration : IEntityTypeConfiguration<PullRequestComment>
{
    public void Configure(EntityTypeBuilder<PullRequestComment> builder)
    {
        builder.ToTable("pull_request_comments");
        builder.HasKey(c => new { c.RepositoryId, c.Number });
        builder.HasOne<Repository>().WithMany().HasForeignKey(c => c.RepositoryId).OnDelete(DeleteBehavior.Cascade);
    }
}
