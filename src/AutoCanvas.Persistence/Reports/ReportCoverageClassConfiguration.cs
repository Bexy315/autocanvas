using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Reports;

internal sealed class ReportCoverageClassConfiguration : IEntityTypeConfiguration<ReportCoverageClass>
{
    public void Configure(EntityTypeBuilder<ReportCoverageClass> builder)
    {
        builder.ToTable("report_coverage_classes");
        builder.HasKey(x => new { x.ReportId, x.Ordinal });
        builder.Property(x => x.Name).HasMaxLength(ReportCoverageClass.MaxNameLength);
        builder.Property(x => x.File).HasMaxLength(ReportCoverageClass.MaxNameLength);
        builder.Property(x => x.Uncovered).HasMaxLength(ReportCoverageClass.MaxUncoveredLength);
        builder.HasOne(x => x.Report).WithMany().HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Cascade);
    }
}
