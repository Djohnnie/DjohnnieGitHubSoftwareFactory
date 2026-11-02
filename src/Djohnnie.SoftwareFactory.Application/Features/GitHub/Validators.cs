using Djohnnie.SoftwareFactory.Contracts.GitHub;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed class GetRepositoryValidator : AbstractValidator<GetRepositoryQuery>
{
    public GetRepositoryValidator() =>
        RuleFor(request => request.Dto.RepositoryFullName).NotEmpty().MaximumLength(200)
            .Matches(@"^[a-zA-Z0-9_.-]+/[a-zA-Z0-9_.-]+$");
}

public sealed class RepositoryPathValidator : AbstractValidator<RepositoryPathRequest>
{
    public RepositoryPathValidator()
    {
        RuleFor(request => request.RepositoryFullName).NotEmpty();
        RuleFor(request => request.Path).NotEmpty();
    }
}

public sealed class GetDirectoryValidator : AbstractValidator<GetDirectoryQuery>
{
    public GetDirectoryValidator() => RuleFor(request => request.Dto).NotNull().SetValidator(new RepositoryPathValidator());
}

public sealed class GetFileValidator : AbstractValidator<GetFileQuery>
{
    public GetFileValidator() => RuleFor(request => request.Dto).NotNull().SetValidator(new RepositoryPathValidator());
}

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

public sealed class DeleteFileValidator : AbstractValidator<DeleteFileCommand>
{
    public DeleteFileValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).NotEmpty();
        RuleFor(request => request.Dto.Path).NotEmpty();
        RuleFor(request => request.Dto.Message).NotEmpty();
        RuleFor(request => request.Dto.Sha).NotEmpty();
    }
}

public sealed class GetCustomAgentsValidator : AbstractValidator<GetCustomAgentsQuery>
{
    public GetCustomAgentsValidator() => RuleFor(request => request.Dto.RepositoryFullName).NotEmpty();
}
