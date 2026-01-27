using System.Security.Cryptography.X509Certificates;

namespace AetherGuard.Core.Security;

public static class MtlsCertificateLoader
{
    public static X509Certificate2 LoadServerCertificate(MtlsOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.CertificatePath)
            || string.IsNullOrWhiteSpace(options.KeyPath))
        {
            throw new InvalidOperationException("mTLS is enabled but certificate paths are not configured.");
        }

        return X509Certificate2.CreateFromPemFile(options.CertificatePath, options.KeyPath);
    }

    public static bool ValidateClientCertificate(X509Certificate2? certificate, MtlsOptions options)
    {
        if (certificate is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(options.BundlePath) || !File.Exists(options.BundlePath))
        {
            return false;
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(LoadBundle(options.BundlePath));

        if (!chain.Build(certificate))
        {
            return false;
        }

        if (options.AllowedClientSpiffeIds.Count == 0)
        {
            return true;
        }

        var spiffeId = SpiffeIdParser.GetSpiffeId(certificate);
        if (string.IsNullOrWhiteSpace(spiffeId))
        {
            return false;
        }

        return options.AllowedClientSpiffeIds.Any(allowed =>
            string.Equals(allowed, spiffeId, StringComparison.OrdinalIgnoreCase));
    }

    private static X509Certificate2Collection LoadBundle(string bundlePath)
    {
        var collection = new X509Certificate2Collection();
        collection.ImportFromPemFile(bundlePath);
        return collection;
    }
}
