using DocuAI.Domain.Common;

namespace DocuAI.UnitTests.Domain;

public sealed class ResultTests
{
    private static readonly Error SampleError = Error.NotFound("Sample.NotFound", "No encontrado");

    [Fact]
    public void Success_DebeExponerElValor()
    {
        Result<int> result = 42;

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
        result.Error.ShouldBe(Error.None);
    }

    [Fact]
    public void Failure_AccederAlValor_DebeLanzarInvalidOperation()
    {
        Result<int> result = SampleError;

        result.IsFailure.ShouldBeTrue();
        Should.Throw<InvalidOperationException>(() => _ = result.Value);
    }

    [Fact]
    public void Map_SobreFallo_DebePropagarElError()
    {
        Result<int> result = SampleError;

        var mapped = result.Map(value => value.ToString());

        mapped.IsFailure.ShouldBeTrue();
        mapped.Error.ShouldBe(SampleError);
    }

    [Fact]
    public void CreateFailure_ViaContratoEstatico_DebeCrearElTipoConcreto()
    {
        var failure = Create<Result<string>>(SampleError);

        failure.ShouldBeOfType<Result<string>>();
        failure.Error.ShouldBe(SampleError);

        static T Create<T>(Error error) where T : IResultFactory<T> => T.CreateFailure(error);
    }
}
