using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Repositories;

internal sealed class RepositoryPushConfiguration : IEntityTypeConfiguration<RepositoryPush>
{
    public void Configure(EntityTypeBuilder<RepositoryPush> builder)
    {
        builder.ToTable("repository_pushes");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Ref).HasMaxLength(RepositoryRef.MaxRefLength);
        builder.Property(p => p.Before).HasMaxLength(Repository.MaxCommitLength);
        builder.Property(p => p.After).HasMaxLength(Repository.MaxCommitLength);
        builder.Property(p => p.Pusher).HasMaxLength(RepositoryPush.MaxPusherLength);
        builder.Property(p => p.Message).HasMaxLength(RepositoryPush.MaxMessageLength);
        builder.HasIndex(p => p.ReceivedAt);
        builder.HasOne<Repository>().WithMany().HasForeignKey(p => p.RepositoryId).OnDelete(DeleteBehavior.Cascade);
    }
}
