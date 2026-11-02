using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Epics;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.Epics;

public sealed class SaveEpicValidator : AbstractValidator<SaveEpicCommand>
{
    public SaveEpicValidator()
    {
        RuleFor(request => request.Dto).NotNull().SetValidator(new ContractValidator<Epic>());
        RuleFor(request => request.Dto.Id).GreaterThanOrEqualTo(0);
    }
}

public sealed class GetEpicsValidator : AbstractValidator<GetEpicsQuery>
{
    public GetEpicsValidator() => RuleFor(request => request.Dto.RepositoryFullName).NotEmpty().MaximumLength(200);
}

public sealed class GetEpicValidator : AbstractValidator<GetEpicQuery>
{
    public GetEpicValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).NotEmpty().MaximumLength(200);
        RuleFor(request => request.Dto.Id).GreaterThan(0);
    }
}

public sealed class AcceptEpicValidator : AbstractValidator<AcceptEpicCommand>
{
    public AcceptEpicValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).NotEmpty().MaximumLength(200);
        RuleFor(request => request.Dto.Id).GreaterThan(0);
    }
}

public sealed class AssignEpicAgentValidator : AbstractValidator<AssignEpicAgentCommand>
{
    public AssignEpicAgentValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).NotEmpty().MaximumLength(200);
        RuleFor(request => request.Dto.Id).GreaterThan(0);
    }
}

public sealed class DeleteEpicValidator : AbstractValidator<DeleteEpicCommand>
{
    public DeleteEpicValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).NotEmpty().MaximumLength(200);
        RuleFor(request => request.Dto.Id).GreaterThan(0);
    }
}
