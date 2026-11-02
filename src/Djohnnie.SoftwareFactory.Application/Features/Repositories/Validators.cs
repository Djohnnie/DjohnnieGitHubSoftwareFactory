using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.Repositories;

public sealed class RecordRepositoryAccessValidator : AbstractValidator<RecordRepositoryAccessCommand>
{
    public RecordRepositoryAccessValidator() =>
        RuleFor(request => request.Dto.RepositoryFullName).NotEmpty().MaximumLength(200)
            .Matches(@"^[a-zA-Z0-9_.-]+/[a-zA-Z0-9_.-]+$");
}
