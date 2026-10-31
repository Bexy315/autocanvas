using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Reports;

internal sealed class ReportCoveragePackageConfiguration : IEntityTypeConfiguration<ReportCoveragePackage>
{
    public void Configure(EntityTypeBuilder<ReportCoveragePackage> builder)
    {
        builder.ToTable("report_coverage_packages");
        builder.HasKey(x => new { x.ReportId, x.Ordinal });
        builder.Property(x => x.Name).HasMaxLength(ReportCoveragePackage.MaxNameLength);
        builder.HasOne(x => x.Report).WithMany().HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Cascade);
    }
}
