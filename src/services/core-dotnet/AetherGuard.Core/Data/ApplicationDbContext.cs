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
    public DbSet<ExternalSignalFeedState> ExternalSignalFeedStates => Set<ExternalSignalFeedState>();
    public DbSet<ExternalSignal> ExternalSignals => Set<ExternalSignal>();
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

        modelBuilder.Entity<ExternalSignal>(entity =>
        {
            entity.ToTable("external_signals");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Source, e.ExternalId }).IsUnique();
            entity.HasIndex(e => e.PublishedAt);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.Source).HasColumnName("source");
            entity.Property(e => e.ExternalId).HasColumnName("external_id");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.Summary).HasColumnName("summary");
            entity.Property(e => e.SummaryDigest).HasColumnName("summary_digest");
            entity.Property(e => e.SummaryDigestTruncated).HasColumnName("summary_digest_truncated");
            entity.Property(e => e.SummarySchemaVersion).HasColumnName("summary_schema_version");
            entity.Property(e => e.EnrichmentSchemaVersion).HasColumnName("enrichment_schema_version");
            entity.Property(e => e.SentimentNegative).HasColumnName("sentiment_negative");
            entity.Property(e => e.SentimentNeutral).HasColumnName("sentiment_neutral");
            entity.Property(e => e.SentimentPositive).HasColumnName("sentiment_positive");
            entity.Property(e => e.VolatilityProbability).HasColumnName("volatility_probability");
            entity.Property(e => e.SupplyBias).HasColumnName("supply_bias");
            entity.Property(e => e.SummarizedAt).HasColumnName("summarized_at");
            entity.Property(e => e.EnrichedAt).HasColumnName("enriched_at");
            entity.Property(e => e.Region).HasColumnName("region");
            entity.Property(e => e.Severity).HasColumnName("severity");
            entity.Property(e => e.Category).HasColumnName("category");
            entity.Property(e => e.Url).HasColumnName("url");
            entity.Property(e => e.Tags).HasColumnName("tags");
            entity.Property(e => e.PublishedAt).HasColumnName("published_at");
            entity.Property(e => e.IngestedAt).HasColumnName("ingested_at");
        });

        modelBuilder.Entity<ExternalSignalFeedState>(entity =>
        {
            entity.ToTable("external_signal_feeds");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Url).HasColumnName("url");
            entity.Property(e => e.LastFetchAt).HasColumnName("last_fetch_at");
            entity.Property(e => e.LastSuccessAt).HasColumnName("last_success_at");
            entity.Property(e => e.FailureCount).HasColumnName("failure_count");
            entity.Property(e => e.LastError).HasColumnName("last_error");
            entity.Property(e => e.LastStatusCode).HasColumnName("last_status_code");
        });
    }
}
