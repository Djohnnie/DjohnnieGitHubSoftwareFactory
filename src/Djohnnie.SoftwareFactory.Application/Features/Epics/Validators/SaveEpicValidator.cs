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
