using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Triggers;

internal sealed class AutomationScheduleConfiguration : IEntityTypeConfiguration<AutomationSchedule>
{
    public void Configure(EntityTypeBuilder<AutomationSchedule> builder)
    {
        builder.ToTable("automation_schedules");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Cron).HasMaxLength(AutomationSchedule.MaxTextLength);
        builder.Property(s => s.TimeZone).HasMaxLength(AutomationSchedule.MaxTextLength);
        builder.HasIndex(s => s.NextAt);
        builder.HasIndex(s => s.AutomationId);
        builder.HasOne(s => s.Automation).WithMany().HasForeignKey(s => s.AutomationId).OnDelete(DeleteBehavior.Cascade);
    }
}
