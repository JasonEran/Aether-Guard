using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AetherGuard.Core.Models;

[Table("TelemetryRecords")]
public class TelemetryRecord
{
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("Id")]
    public long Id { get; set; }

    [Column("AgentId")]
    public string AgentId { get; set; } = string.Empty;

    [Column("CpuUsage")]
    public double CpuUsage { get; set; }

    [Column("MemoryUsage")]
    public double MemoryUsage { get; set; }

    [Column("AiStatus")]
    public string AiStatus { get; set; } = string.Empty;

    [Column("AiConfidence")]
    public double AiConfidence { get; set; }

    [Column("RootCause")]
    public string? RootCause { get; set; }

    [Column("PredictedCpu")]
    public double? PredictedCpu { get; set; }

    [Column("Timestamp")]
    public DateTime Timestamp { get; set; }
}
