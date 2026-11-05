using Djohnnie.SoftwareFactory.Application.Features.Repositories;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.Agents;

public sealed class SyncAgentsValidator : AbstractValidator<SyncAgentsCommand>
{
    public SyncAgentsValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.FileNames).NotNull();
        RuleForEach(request => request.Dto.FileNames).NotEmpty().MaximumLength(100);
    }
}
