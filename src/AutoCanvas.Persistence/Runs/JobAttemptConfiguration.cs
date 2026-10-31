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
        builder.HasIndex(a => new { a.RunJobId, a.Number }).IsUnique();
        builder.HasOne(a => a.Job).WithMany().HasForeignKey(a => a.RunJobId).OnDelete(DeleteBehavior.Cascade);

        // What the queue looks for: queued attempts whose time has come.
        builder.HasIndex(a => new { a.Status, a.NotBefore });
    }
}
