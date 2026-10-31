using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Runs;

internal sealed class LogChunkConfiguration : IEntityTypeConfiguration<LogChunk>
{
    public void Configure(EntityTypeBuilder<LogChunk> builder)
    {
        builder.ToTable("log_chunks");
        builder.HasKey(c => new { c.AttemptId, c.Sequence });
        builder.Property(c => c.Lines).HasMaxLength(LogChunk.MaxLinesLength);
        builder.HasOne(c => c.Attempt).WithMany().HasForeignKey(c => c.AttemptId).OnDelete(DeleteBehavior.Cascade);
    }
}
