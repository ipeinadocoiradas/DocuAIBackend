using DocuAI.Domain.Common;
using MediatR;

namespace DocuAI.Application.Abstractions.Messaging;

/// <summary>Comando sin valor de retorno (modifica estado).</summary>
public interface ICommand : IRequest<Result>
{
}

/// <summary>Comando que devuelve un valor tras modificar estado.</summary>
public interface ICommand<TResponse> : IRequest<Result<TResponse>>
{
}

/// <summary>Consulta de solo lectura.</summary>
public interface IQuery<TResponse> : IRequest<Result<TResponse>>
{
}

/// <summary>Manejador de <see cref="ICommand"/>.</summary>
public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand, Result>
    where TCommand : ICommand
{
}

/// <summary>Manejador de <see cref="ICommand{TResponse}"/>.</summary>
public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>
{
}

/// <summary>Manejador de <see cref="IQuery{TResponse}"/>.</summary>
public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>
{
}
