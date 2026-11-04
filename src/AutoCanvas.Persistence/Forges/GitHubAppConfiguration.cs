using AutoCanvas.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Forges;

internal sealed class GitHubAppConfiguration : IEntityTypeConfiguration<GitHubApp>
{
    public void Configure(EntityTypeBuilder<GitHubApp> builder)
    {
        builder.ToTable("github_apps");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Slug).HasMaxLength(GitHubApp.MaxNameLength);
        builder.Property(a => a.Name).HasMaxLength(GitHubApp.MaxNameLength);
        builder.Property(a => a.ClientId).HasMaxLength(GitHubApp.MaxNameLength);
        builder.Property(a => a.OwnerLogin).HasMaxLength(GitHubApp.MaxNameLength);
        builder.Property(a => a.HtmlUrl).HasMaxLength(GitHubApp.MaxAddressLength);
        builder.Property(a => a.WebAddress).HasMaxLength(GitHubApp.MaxAddressLength);
        builder.Property(a => a.ApiAddress).HasMaxLength(GitHubApp.MaxAddressLength);
        builder.Property(a => a.PrivateKeyCiphertext).HasMaxLength(GitHubApp.MaxSecretLength);
        builder.Property(a => a.WebhookSecretCiphertext).HasMaxLength(GitHubApp.MaxNameLength * 2);
        builder.Property(a => a.ClientSecretCiphertext).HasMaxLength(GitHubApp.MaxNameLength * 2);
        builder.Property(a => a.CreatedByName).HasMaxLength(User.MaxUserNameLength);
    }
}
