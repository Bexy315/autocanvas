using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Variables;

internal sealed class MasterKeyCheckConfiguration : IEntityTypeConfiguration<MasterKeyCheck>
{
    public void Configure(EntityTypeBuilder<MasterKeyCheck> builder)
    {
        builder.ToTable("master_key_checks");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.KeyId).HasMaxLength(Secret.MaxKeyIdLength);
    }
}
