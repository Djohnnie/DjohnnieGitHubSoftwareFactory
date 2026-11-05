using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.Repositories;

public sealed class RecordRepositoryAccessValidator : AbstractValidator<RecordRepositoryAccessCommand>
{
    public RecordRepositoryAccessValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
    }
}
