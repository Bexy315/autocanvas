using AutoCanvas.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Variables;

internal sealed class VariableConfiguration : IEntityTypeConfiguration<Variable>
{
    public void Configure(EntityTypeBuilder<Variable> builder)
    {
        builder.ToTable("variables");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();
        builder.Property(v => v.Scope).HasMaxLength(ValueScopes.MaxLength);
        builder.Property(v => v.Name).HasMaxLength(Variable.MaxNameLength);
        builder.Property(v => v.NormalizedName).HasMaxLength(Variable.MaxNameLength);
        builder.Property(v => v.UpdatedByName).HasMaxLength(User.MaxUserNameLength);
        builder.HasIndex(v => new { v.Scope, v.NormalizedName }).IsUnique();

        builder.HasOne(v => v.Folder).WithMany().HasForeignKey(v => v.FolderId).OnDelete(DeleteBehavior.Cascade);
    }
}
