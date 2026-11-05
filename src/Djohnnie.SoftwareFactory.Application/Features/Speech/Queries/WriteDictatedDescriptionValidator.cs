using Djohnnie.SoftwareFactory.Contracts.Speech;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.Speech;

public sealed class WriteDictatedDescriptionValidator : AbstractValidator<WriteDictatedDescriptionQuery>
{
    public WriteDictatedDescriptionValidator()
    {
        RuleFor(request => request.Dto).NotNull();
        When(request => request.Dto is not null, () =>
        {
            RuleFor(request => request.Dto.Transcript).NotEmpty().WithMessage("Nothing was dictated.")
                .MaximumLength(WriteDictatedDescriptionRequest.MaxTranscriptLength);
            RuleFor(request => request.Dto.Subject).NotEmpty().MaximumLength(100);
            RuleFor(request => request.Dto.Title).MaximumLength(256);
            RuleFor(request => request.Dto.Description).MaximumLength(WorkItem.MaxDescriptionLength);
            RuleFor(request => request.Dto.Template).MaximumLength(WorkItem.MaxDescriptionLength);
        });
    }
}
