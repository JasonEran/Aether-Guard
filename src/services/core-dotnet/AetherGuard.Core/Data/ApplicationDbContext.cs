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
}
