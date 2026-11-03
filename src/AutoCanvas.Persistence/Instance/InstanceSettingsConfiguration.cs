using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Instance;

internal sealed class InstanceSettingsConfiguration : IEntityTypeConfiguration<InstanceSettings>
{
    public void Configure(EntityTypeBuilder<InstanceSettings> builder)
    {
        builder.ToTable("instance_settings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.PublicUrl).HasMaxLength(InstanceSettings.MaxUrlLength);
        builder.Property(s => s.RunnerUrl).HasMaxLength(InstanceSettings.MaxUrlLength);
    }
}
