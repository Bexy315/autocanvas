using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Runs;

internal sealed class JobStepConfiguration : IEntityTypeConfiguration<JobStep>
{
    public void Configure(EntityTypeBuilder<JobStep> builder)
    {
        builder.ToTable("job_steps");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.StepId).HasMaxLength(JobStep.MaxStepIdLength);
        builder.Property(s => s.Name).HasMaxLength(JobStep.MaxNameLength);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Reason).HasMaxLength(JobStep.MaxReasonLength);
        builder.Property(s => s.Error).HasMaxLength(JobStep.MaxReasonLength);
        builder.Property(s => s.Outputs).HasMaxLength(JobStep.MaxOutputsLength);
        builder.HasIndex(s => new { s.AttemptId, s.Index }).IsUnique();
        builder.HasOne(s => s.Attempt).WithMany().HasForeignKey(s => s.AttemptId).OnDelete(DeleteBehavior.Cascade);
    }
}
