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
    public DbSet<AgentCommand> AgentCommands => Set<AgentCommand>();
    public DbSet<CommandAudit> CommandAudits => Set<CommandAudit>();
    public DbSet<TelemetryRecord> TelemetryRecords => Set<TelemetryRecord>();
    public DbSet<SchemaRegistryEntry> SchemaRegistryEntries => Set<SchemaRegistryEntry>();

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
            entity.HasKey(e => new { e.Id, e.Timestamp });
            entity.Property(e => e.Id).HasColumnName("Id").ValueGeneratedOnAdd();
            entity.Property(e => e.AgentId).HasColumnName("AgentId");
            entity.Property(e => e.WorkloadTier).HasColumnName("WorkloadTier");
            entity.Property(e => e.RebalanceSignal).HasColumnName("RebalanceSignal");
            entity.Property(e => e.DiskAvailable).HasColumnName("DiskAvailable");
            entity.Property(e => e.AiStatus).HasColumnName("AiStatus");
            entity.Property(e => e.AiConfidence).HasColumnName("AiConfidence");
            entity.Property(e => e.RootCause).HasColumnName("RootCause");
            entity.Property(e => e.PredictedCpu).HasColumnName("PredictedCpu");
            entity.Property(e => e.Timestamp).HasColumnName("Timestamp");
        });

        modelBuilder.Entity<AgentCommand>(entity =>
        {
            entity.ToTable("agent_commands");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CommandId).HasColumnName("command_id");
            entity.Property(e => e.AgentId).HasColumnName("agent_id");
            entity.Property(e => e.WorkloadId).HasColumnName("workload_id");
            entity.Property(e => e.Action).HasColumnName("action");
            entity.Property(e => e.Parameters).HasColumnName("parameters");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.Nonce).HasColumnName("nonce");
            entity.Property(e => e.Signature).HasColumnName("signature");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<CommandAudit>(entity =>
        {
            entity.ToTable("command_audits");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.CommandId).HasColumnName("command_id");
            entity.Property(e => e.Actor).HasColumnName("actor");
            entity.Property(e => e.Action).HasColumnName("action");
            entity.Property(e => e.Result).HasColumnName("result");
            entity.Property(e => e.Error).HasColumnName("error");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<SchemaRegistryEntry>(entity =>
        {
            entity.ToTable("schema_registry");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Subject, e.Version }).IsUnique();
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.Subject).HasColumnName("subject");
            entity.Property(e => e.Version).HasColumnName("version");
            entity.Property(e => e.Schema).HasColumnName("schema");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        });
    }
}
