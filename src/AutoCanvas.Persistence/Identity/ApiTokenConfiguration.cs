using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Identity;

internal sealed class ApiTokenConfiguration : IEntityTypeConfiguration<ApiToken>
{
    public void Configure(EntityTypeBuilder<ApiToken> builder)
    {
        builder.ToTable("api_tokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Name).HasMaxLength(ApiToken.MaxNameLength);
        builder.Property(t => t.TokenHash).HasMaxLength(Session.TokenHashLength).IsFixedLength();
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.Property(t => t.Hint).HasMaxLength(ApiToken.MaxHintLength);
        builder.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
