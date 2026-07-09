using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pismolet.Web.Infrastructure.Postmaster;

#nullable disable

namespace Pismolet.Web.Infrastructure.Migrations.Postmaster;

[DbContext(typeof(MailruPostmasterDbContext))]
public partial class MailruPostmasterDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasAnnotation("ProductVersion", "9.0.17")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

        modelBuilder.Entity<MailruPostmasterDomainDailyMetricEntity>(entity =>
        {
            entity.ToTable("mailru_postmaster_domain_daily_metrics");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.Domain, x.Date }).IsUnique();
            entity.HasIndex(x => x.Date);
            entity.Property(x => x.Domain).HasMaxLength(253).IsRequired();
            entity.Property(x => x.Date).HasColumnType("date").IsRequired();
        });

        modelBuilder.Entity<MailruPostmasterTroubleSnapshotEntity>(entity =>
        {
            entity.ToTable("mailru_postmaster_trouble_snapshots");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.Domain, x.Code }).IsUnique();
            entity.HasIndex(x => new { x.Domain, x.IsActive });
            entity.Property(x => x.Domain).HasMaxLength(253).IsRequired();
            entity.Property(x => x.Message).HasMaxLength(1000).IsRequired();
        });

        modelBuilder.Entity<MailruPostmasterSyncStateEntity>(entity =>
        {
            entity.ToTable("mailru_postmaster_sync_states");
            entity.HasKey(x => x.Domain);
            entity.Property(x => x.Domain).HasMaxLength(253).IsRequired();
            entity.Property(x => x.LastErrorCode).HasMaxLength(120);
            entity.Property(x => x.LastErrorSummary).HasMaxLength(1000);
            entity.Property(x => x.LastDomainDate).HasColumnType("date");
        });
    }
}
