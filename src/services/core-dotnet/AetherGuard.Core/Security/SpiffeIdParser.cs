using System.Formats.Asn1;
using System.Security.Cryptography.X509Certificates;

namespace AetherGuard.Core.Security;

public static class SpiffeIdParser
{
    private static readonly Asn1Tag UriTag = new(TagClass.ContextSpecific, 6);

    public static string? GetSpiffeId(X509Certificate2 certificate)
    {
        var extension = certificate.Extensions["2.5.29.17"];
        if (extension is null)
        {
            return null;
        }

        var reader = new AsnReader(extension.RawData, AsnEncodingRules.DER);
        var sequence = reader.ReadSequence();
        while (sequence.HasData)
        {
            var tag = sequence.PeekTag();
            if (tag.HasSameClassAndValue(UriTag))
            {
                var uri = sequence.ReadCharacterString(UniversalTagNumber.IA5String, UriTag);
                if (!string.IsNullOrWhiteSpace(uri)
                    && uri.StartsWith("spiffe://", StringComparison.OrdinalIgnoreCase))
                {
                    return uri;
                }
            }
            else
            {
                sequence.ReadEncodedValue();
            }
        }

        return null;
    }
}
