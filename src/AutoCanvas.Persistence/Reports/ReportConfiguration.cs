using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Reports;

internal sealed class ReportConfiguration : IEntityTypeConfiguration<Report>
{
    public void Configure(EntityTypeBuilder<Report> builder)
    {
        builder.ToTable("reports");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.JobKey).HasMaxLength(Runs.RunJob.MaxKeyLength);
        builder.Property(r => r.Name).HasMaxLength(Report.MaxNameLength);
        builder.Property(r => r.Format).HasMaxLength(20);
        builder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Path).HasMaxLength(Report.MaxPathLength);
        builder.Property(r => r.Error).HasMaxLength(Report.MaxErrorLength);
        builder.Property(r => r.Warnings).HasMaxLength(Report.MaxWarningsLength);

        // A runner that uploads a file again after a reconnect gets the stored report back.
        builder.HasIndex(r => new { r.AttemptId, r.Name, r.Path }).IsUnique();
        builder.HasIndex(r => r.RunId);
        builder.HasOne(r => r.Attempt).WithMany().HasForeignKey(r => r.AttemptId).OnDelete(DeleteBehavior.Cascade);
    }
}
