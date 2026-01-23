using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AetherGuard.Core.Models;

[Table("agent_commands")]
public class AgentCommand
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    [Column("command_id")]
    public Guid CommandId { get; set; }

    [Column("agent_id")]
    public Guid AgentId { get; set; }

    [Column("workload_id")]
    public string WorkloadId { get; set; } = string.Empty;

    [Column("action")]
    public string Action { get; set; } = string.Empty;

    [Column("parameters")]
    public string Parameters { get; set; } = "{}";

    [Column("status")]
    public string Status { get; set; } = "PENDING";

    [Column("nonce")]
    public string Nonce { get; set; } = string.Empty;

    [Column("signature")]
    public string Signature { get; set; } = string.Empty;

    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
