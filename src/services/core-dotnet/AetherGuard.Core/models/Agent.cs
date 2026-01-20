using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace AetherGuard.Core.Models;

[Index(nameof(AgentToken), IsUnique = true)]
public class Agent
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public string AgentToken { get; set; } = string.Empty;

    [Required]
    public string Hostname { get; set; } = string.Empty;

    [Required]
    public string Status { get; set; } = "OFFLINE";

    public DateTimeOffset LastHeartbeat { get; set; }
}
