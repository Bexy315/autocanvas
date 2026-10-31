using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Runs;

internal sealed class RunJobConfiguration : IEntityTypeConfiguration<RunJob>
{
    public void Configure(EntityTypeBuilder<RunJob> builder)
    {
        builder.ToTable("run_jobs");
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).ValueGeneratedNever();
        builder.Property(j => j.Key).HasMaxLength(RunJob.MaxKeyLength);
        builder.Property(j => j.Name).HasMaxLength(RunJob.MaxNameLength);
        builder.Property(j => j.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(j => j.ActivatedCase).HasMaxLength(RunJob.MaxNameLength);
        builder.Property(j => j.Reason).HasMaxLength(RunJob.MaxReasonLength);
        builder.HasIndex(j => new { j.RunId, j.Key }).IsUnique();
        builder.HasOne(j => j.Run).WithMany().HasForeignKey(j => j.RunId).OnDelete(DeleteBehavior.Cascade);
    }
}
