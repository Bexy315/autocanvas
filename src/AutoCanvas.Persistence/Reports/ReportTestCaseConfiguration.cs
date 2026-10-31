using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Reports;

internal sealed class ReportTestCaseConfiguration : IEntityTypeConfiguration<ReportTestCase>
{
    public void Configure(EntityTypeBuilder<ReportTestCase> builder)
    {
        builder.ToTable("report_test_cases");
        builder.HasKey(x => new { x.ReportId, x.Ordinal });
        builder.Property(x => x.Group).HasMaxLength(ReportTestCase.MaxNameLength);
        builder.Property(x => x.Suite).HasMaxLength(ReportTestCase.MaxNameLength);
        builder.Property(x => x.Name).HasMaxLength(ReportTestCase.MaxNameLength);
        builder.Property(x => x.FullName).HasMaxLength(ReportTestCase.MaxFullNameLength);
        builder.Property(x => x.NameHash).HasMaxLength(64);
        builder.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Message).HasMaxLength(ReportTestCase.MaxMessageLength);
        builder.Property(x => x.StackTrace).HasMaxLength(ReportTestCase.MaxStackTraceLength);
        builder.Property(x => x.Output).HasMaxLength(ReportTestCase.MaxMessageLength);
        builder.HasIndex(x => x.NameHash);
        builder.HasOne(x => x.Report).WithMany().HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Cascade);
    }
}
