using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Runs;

internal sealed class JobAttemptConfiguration : IEntityTypeConfiguration<JobAttempt>
{
    public void Configure(EntityTypeBuilder<JobAttempt> builder)
    {
        builder.ToTable("job_attempts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Labels).HasMaxLength(JobAttempt.MaxRequirementLength);
        builder.Property(a => a.NodeTypes).HasMaxLength(JobAttempt.MaxRequirementLength);
        builder.Property(a => a.Error).HasMaxLength(JobAttempt.MaxErrorLength);
        builder.Property(a => a.CancelReason).HasMaxLength(JobAttempt.MaxErrorLength);
        builder.Property(a => a.Warnings).HasMaxLength(JobAttempt.MaxWarningsLength);
        builder.HasIndex(a => new { a.RunJobId, a.Number }).IsUnique();
        builder.HasOne(a => a.Job).WithMany().HasForeignKey(a => a.RunJobId).OnDelete(DeleteBehavior.Cascade);

        // What the queue looks for: queued attempts whose time has come.
        builder.HasIndex(a => new { a.Status, a.NotBefore });

        // What the sweeper and heartbeats look for: attempts a runner holds.
        builder.HasIndex(a => new { a.RunnerId, a.Status });
        builder.HasIndex(a => new { a.Status, a.LeaseExpiresAt });
    }
}
