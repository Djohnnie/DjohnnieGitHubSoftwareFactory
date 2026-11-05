using FluentValidation;
using MediatR;
using Djohnnie.SoftwareFactory.Contracts;

namespace Djohnnie.SoftwareFactory.Application.Common;

public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var context = new ValidationContext<TRequest>(request);
        var errors = new List<string>();
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(context, cancellationToken);
            errors.AddRange(result.Errors.Select(error => error.ErrorMessage));
        }
        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors.Distinct().ToList());
        }
        return await next();
    }
}
