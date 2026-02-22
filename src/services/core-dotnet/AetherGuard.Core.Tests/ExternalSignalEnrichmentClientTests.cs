using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AetherGuard.Core.Models;
using AetherGuard.Core.Services.ExternalSignals;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AetherGuard.Core.Tests;

public class ExternalSignalEnrichmentClientTests
{
    [Fact]
    public async Task EnrichBatchAsync_ReturnsVectors_WhenPayloadIsValid()
    {
        var json = """
                   {
                     "schemaVersion": "1.0",
                     "vectors": [
                       { "index": 0, "S_v": [0.6, 0.3, 0.1], "P_v": 0.72, "B_s": 0.12 },
                       { "index": 1, "S_v": [0.2, 0.5, 0.3], "P_v": 0.35, "B_s": 0.40 }
                     ]
                   }
                   """;

        var client = CreateClient((request, _) =>
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/signals/enrich/batch", StringComparison.OrdinalIgnoreCase) != true)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        var signals = new[]
        {
            CreateSignal("signal-1"),
            CreateSignal("signal-2")
        };

        var result = await client.EnrichBatchAsync(signals, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("1.0", result!.SchemaVersion);
        Assert.Equal(2, result.Vectors.Count);
        Assert.Equal(0, result.Vectors[0].Index);
        Assert.Equal(3, result.Vectors[0].Vector.SentimentVector.Length);
        Assert.Equal(0.72, result.Vectors[0].Vector.VolatilityProbability, 3);
    }

    [Fact]
    public async Task EnrichBatchAsync_FiltersInvalidVectors()
    {
        var json = """
                   {
                     "schemaVersion": "1.0",
                     "vectors": [
                       { "index": 0, "S_v": [0.6, 0.3], "P_v": 0.72, "B_s": 0.12 },
                       { "index": 1, "S_v": [0.2, 0.5, 0.3], "P_v": 0.35, "B_s": 0.40 }
                     ]
                   }
                   """;

        var client = CreateClient((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

        var signals = new[]
        {
            CreateSignal("signal-1"),
            CreateSignal("signal-2")
        };

        var result = await client.EnrichBatchAsync(signals, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result!.Vectors);
        Assert.Equal(1, result.Vectors[0].Index);
    }

    [Fact]
    public async Task EnrichAsync_ClampsOutOfRangeValues()
    {
        var json = """
                   {
                     "schemaVersion": "1.0",
                     "S_v": [0.6, 0.3, 0.1],
                     "P_v": 2.5,
                     "B_s": -1.0
                   }
                   """;

        var client = CreateClient((request, _) =>
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/signals/enrich", StringComparison.OrdinalIgnoreCase) != true)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        var result = await client.EnrichAsync(CreateSignal("signal-1"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1.0, result!.VolatilityProbability, 3);
        Assert.Equal(0.0, result.SupplyBias, 3);
    }

    private static ExternalSignalEnrichmentClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
    {
        var options = Options.Create(new ExternalSignalsOptions
        {
            Enrichment = new ExternalSignalEnrichmentOptions
            {
                Enabled = true,
                BaseUrl = "http://localhost:8000",
                TimeoutSeconds = 5,
                MaxBatchSize = 200,
                MaxConcurrency = 4,
                SummaryMaxChars = 280
            }
        });

        var httpClient = new HttpClient(new StubHandler(responder));
        return new ExternalSignalEnrichmentClient(
            httpClient,
            options,
            NullLogger<ExternalSignalEnrichmentClient>.Instance);
    }

    private static ExternalSignal CreateSignal(string externalId)
        => new()
        {
            Source = "aws-status",
            ExternalId = externalId,
            Title = $"Signal {externalId}",
            Summary = "Investigating elevated error rates.",
            PublishedAt = DateTimeOffset.UtcNow
        };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request, cancellationToken));
    }
}
