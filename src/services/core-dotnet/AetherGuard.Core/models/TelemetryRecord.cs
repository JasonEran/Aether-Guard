using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AetherGuard.Core.Models;

public class TelemetryRecord
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public string AgentId { get; set; } = string.Empty;

    public double CpuUsage { get; set; }

    public double MemoryUsage { get; set; }

    public string AiStatus { get; set; } = string.Empty;

    public double AiConfidence { get; set; }

    public DateTime Timestamp { get; set; }
}
