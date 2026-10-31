using AutoCanvas.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Variables;

internal sealed class SecretConfiguration : IEntityTypeConfiguration<Secret>
{
    public void Configure(EntityTypeBuilder<Secret> builder)
    {
        builder.ToTable("secrets");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Scope).HasMaxLength(ValueScopes.MaxLength);
        builder.Property(s => s.Name).HasMaxLength(Secret.MaxNameLength);
        builder.Property(s => s.NormalizedName).HasMaxLength(Secret.MaxNameLength);
        builder.Property(s => s.KeyId).HasMaxLength(Secret.MaxKeyIdLength);
        builder.Property(s => s.UpdatedByName).HasMaxLength(User.MaxUserNameLength);
        builder.HasIndex(s => new { s.Scope, s.NormalizedName }).IsUnique();

        builder.HasOne(s => s.Folder).WithMany().HasForeignKey(s => s.FolderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(s => s.Automation).WithMany().HasForeignKey(s => s.AutomationId).OnDelete(DeleteBehavior.Cascade);
    }
}
