namespace AetherGuard.Core.Security;

public sealed record MtlsOptions
{
    public bool Enabled { get; init; }
    public int Port { get; init; } = 8443;
    public bool AllowHttp { get; init; }
    public int HttpPort { get; init; } = 8080;
    public string? CertificatePath { get; init; }
    public string? KeyPath { get; init; }
    public string? BundlePath { get; init; }
    public bool RequireClientCertificate { get; init; } = true;
    public bool DisableHttpsRedirection { get; init; } = true;
    public List<string> AllowedClientSpiffeIds { get; init; } = new();
}
