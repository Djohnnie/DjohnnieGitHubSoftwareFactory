using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed class PutFileValidator : AbstractValidator<PutFileCommand>
{
    public PutFileValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).NotEmpty();
        RuleFor(request => request.Dto.Path).NotEmpty();
        RuleFor(request => request.Dto.Content).NotNull();
        RuleFor(request => request.Dto.Message).NotEmpty();
    }
}
