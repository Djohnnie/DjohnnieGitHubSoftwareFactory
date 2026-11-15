using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Application.Features.RepositoryAnalysis;
using Djohnnie.SoftwareFactory.Contracts.RepositoryAnalysis;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.TodoTasks;

public sealed class StartRepositoryAnalysisValidator : AbstractValidator<StartRepositoryAnalysisCommand>
{
    public StartRepositoryAnalysisValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.Mode).IsInEnum();
        RuleFor(request => request.Dto.MaxFeatures).InclusiveBetween(1, StartRepositoryAnalysisRequest.MaxMaxFeatures);
        RuleFor(request => request.Dto.PathPrefix)
            .MaximumLength(StartRepositoryAnalysisRequest.MaxPathPrefixLength)
            .Must(prefix => RepositoryTreeFilter.IsValidPrefix(prefix))
            .WithMessage("The folder is a path relative to the repository root and can't contain '..'.");
    }
}

public sealed class GetTodoTaskRunsValidator : AbstractValidator<GetTodoTaskRunsQuery>
{
    public GetTodoTaskRunsValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
    }
}

public sealed class GetTodoTaskRunValidator : AbstractValidator<GetTodoTaskRunQuery>
{
    public GetTodoTaskRunValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.Id).NotEmpty();
    }
}

public sealed class CancelTodoTaskRunValidator : AbstractValidator<CancelTodoTaskRunCommand>
{
    public CancelTodoTaskRunValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.Id).NotEmpty();
    }
}

public sealed class DiscardGeneratedDraftsValidator : AbstractValidator<DiscardGeneratedDraftsCommand>
{
    public DiscardGeneratedDraftsValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.Id).NotEmpty();
    }
}
