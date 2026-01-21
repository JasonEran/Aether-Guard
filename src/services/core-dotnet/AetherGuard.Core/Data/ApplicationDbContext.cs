using AetherGuard.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace AetherGuard.Core.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<TelemetryRecord> TelemetryRecords => Set<TelemetryRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Agent>(entity =>
        {
            entity.ToTable("agents");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AgentToken).HasColumnName("agenttoken");
            entity.Property(e => e.Hostname).HasColumnName("hostname");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.LastHeartbeat).HasColumnName("lastheartbeat");
        });

        modelBuilder.Entity<TelemetryRecord>(entity =>
        {
            entity.ToTable("TelemetryRecords");
            entity.Property(e => e.Id).HasColumnName("Id");
            entity.Property(e => e.AgentId).HasColumnName("AgentId");
            entity.Property(e => e.CpuUsage).HasColumnName("CpuUsage");
            entity.Property(e => e.MemoryUsage).HasColumnName("MemoryUsage");
            entity.Property(e => e.AiStatus).HasColumnName("AiStatus");
            entity.Property(e => e.AiConfidence).HasColumnName("AiConfidence");
            entity.Property(e => e.RootCause).HasColumnName("RootCause");
            entity.Property(e => e.PredictedCpu).HasColumnName("PredictedCpu");
            entity.Property(e => e.Timestamp).HasColumnName("Timestamp");
        });
    }
}
