using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Identity;

internal sealed class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> builder)
    {
        builder.ToTable("groups");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).ValueGeneratedNever();
        builder.Property(g => g.Name).HasMaxLength(Group.MaxNameLength);
        builder.Property(g => g.NormalizedName).HasMaxLength(Group.MaxNameLength);
        builder.HasIndex(g => g.NormalizedName).IsUnique();
        builder.Property(g => g.Description).HasMaxLength(Group.MaxDescriptionLength);
    }
}
