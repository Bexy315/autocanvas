using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Forges;

internal sealed class GitHubDeliveryConfiguration : IEntityTypeConfiguration<GitHubDelivery>
{
    public void Configure(EntityTypeBuilder<GitHubDelivery> builder)
    {
        builder.ToTable("github_deliveries");
        builder.HasKey(d => d.DeliveryId);
        builder.Property(d => d.DeliveryId).HasMaxLength(GitHubDelivery.MaxIdLength);
        builder.HasIndex(d => d.ReceivedAt);
    }
}
