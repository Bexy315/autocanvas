using AutoCanvas.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoCanvas.Persistence.Automations;

internal sealed class AutomationVersionConfiguration : IEntityTypeConfiguration<AutomationVersion>
{
    public void Configure(EntityTypeBuilder<AutomationVersion> builder)
    {
        builder.ToTable("automation_versions");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();
        builder.Property(v => v.Message).HasMaxLength(AutomationVersion.MaxMessageLength);
        builder.Property(v => v.CreatedByName).HasMaxLength(User.MaxUserNameLength);
        builder.Property(v => v.CommitSha).HasMaxLength(Repositories.Repository.MaxCommitLength);
        builder.HasIndex(v => new { v.AutomationId, v.Number }).IsUnique();

        builder.HasOne(v => v.Automation).WithMany().HasForeignKey(v => v.AutomationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(v => v.CreatedById).OnDelete(DeleteBehavior.SetNull);
    }
}
