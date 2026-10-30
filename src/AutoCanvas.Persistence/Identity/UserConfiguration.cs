using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Identity;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.UserName).HasMaxLength(User.MaxUserNameLength);
        builder.Property(u => u.NormalizedUserName).HasMaxLength(User.MaxUserNameLength);
        builder.HasIndex(u => u.NormalizedUserName).IsUnique();
        builder.Property(u => u.DisplayName).HasMaxLength(User.MaxDisplayNameLength);
        builder.Property(u => u.Email).HasMaxLength(User.MaxEmailLength);
        builder.Property(u => u.PasswordHash).HasMaxLength(200);
    }
}
