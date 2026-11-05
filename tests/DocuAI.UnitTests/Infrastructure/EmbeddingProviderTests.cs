using System.Net;
using System.Text;
using DocuAI.Infrastructure.AI.Embeddings;
using DocuAI.Infrastructure.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace DocuAI.UnitTests.Infrastructure;

/// <summary>Pruebas del cliente de embeddings y del health check frente a respuestas con formato Ollama.</summary>
public sealed class EmbeddingProviderTests
{
    private static readonly Uri OllamaBaseAddress = new("http://localhost:11434/v1/");

    private static readonly Microsoft.Extensions.Options.IOptions<EmbeddingOptions> Options =
        MsOptions.Create(new EmbeddingOptions { Model = "bge-m3", Dimensions = 3 });

    private const string OllamaEmbeddingsBody = """
        {"object":"list","model":"bge-m3","data":[
          {"object":"embedding","index":1,"embedding":[0.4,0.5,0.6]},
          {"object":"embedding","index":0,"embedding":[0.1,0.2,0.3]}]}
        """;

    [Fact]
    public async Task GenerateAsync_DebeRespetarElOrdenPorIndice()
    {
        string? sentBody = null;
        var service = CreateService(new StubHandler(HttpStatusCode.OK, OllamaEmbeddingsBody, body => sentBody = body));

        var result = await service.GenerateAsync(["a", "b"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value[0].ShouldBe([0.1f, 0.2f, 0.3f]);
        result.Value[1].ShouldBe([0.4f, 0.5f, 0.6f]);
        sentBody.ShouldNotBeNull();
        sentBody.ShouldContain("\"model\":\"bge-m3\"");
        sentBody.ShouldNotContain("dimensions");
    }

    [Fact]
    public async Task GenerateAsync_ConDimensionIncorrecta_DebeFallar()
    {
        var service = CreateService(new StubHandler(HttpStatusCode.OK, OllamaEmbeddingsBody.Replace(",0.6", string.Empty)));

        var result = await service.GenerateAsync(["a", "b"]);

        result.Error.Code.ShouldBe("Ai.EmbeddingDimensionMismatch");
    }

    [Fact]
    public async Task GenerateAsync_ModeloNoDescargado_DebeDevolverModelNotAvailable()
    {
        var service = CreateService(new StubHandler(HttpStatusCode.NotFound, """{"error":{"message":"model not found"}}"""));

        var result = await service.GenerateAsync(["a"]);

        result.Error.Code.ShouldBe("Ai.ModelNotAvailable");
    }

    [Fact]
    public async Task GenerateAsync_ProveedorCaido_DebeDevolverResultSinLanzar()
    {
        var service = CreateService(new FailingHandler());

        var result = await service.GenerateAsync(["a"]);

        result.Error.Code.ShouldBe("Ai.Unavailable");
    }

    [Theory]
    [InlineData("""{"data":[{"id":"bge-m3:latest"}]}""", HealthStatus.Healthy)]
    [InlineData("""{"data":[{"id":"llama3:latest"}]}""", HealthStatus.Degraded)]
    public async Task HealthCheck_DebeValidarQueElModeloEstaDescargado(string body, HealthStatus expected)
    {
        var healthCheck = new EmbeddingProviderHealthCheck(
            new SingleClientFactory(new HttpClient(new StubHandler(HttpStatusCode.OK, body)) { BaseAddress = OllamaBaseAddress }),
            Options);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.ShouldBe(expected);
    }

    [Fact]
    public async Task HealthCheck_ProveedorCaido_DebeSerUnhealthy()
    {
        var healthCheck = new EmbeddingProviderHealthCheck(
            new SingleClientFactory(new HttpClient(new FailingHandler()) { BaseAddress = OllamaBaseAddress }),
            Options);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.ShouldBe(HealthStatus.Unhealthy);
    }

    private static OpenAiCompatibleEmbeddingService CreateService(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = OllamaBaseAddress },
            Options,
            NullLogger<OpenAiCompatibleEmbeddingService>.Instance);

    private sealed class StubHandler(HttpStatusCode statusCode, string body, Action<string>? captureRequest = null) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                captureRequest?.Invoke(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection refused");
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
