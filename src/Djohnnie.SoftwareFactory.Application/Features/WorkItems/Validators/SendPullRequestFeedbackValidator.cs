using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class SendPullRequestFeedbackValidator : AbstractValidator<SendPullRequestFeedbackCommand>
{
    public SendPullRequestFeedbackValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.Id).NotEmpty();
        RuleFor(request => request.Dto.Body).NotNull().MaximumLength(PullRequestFeedback.MaxBodyLength);
        RuleFor(request => request.Dto.LineComments).NotNull()
            .Must(comments => comments.Count <= PullRequestFeedback.MaxLineComments)
            .WithMessage($"Send at most {PullRequestFeedback.MaxLineComments} line comments at once.");
        RuleFor(request => request.Dto)
            .Must(feedback => !string.IsNullOrWhiteSpace(feedback.Body) || feedback.LineComments is { Count: > 0 })
            .WithName("Feedback")
            .WithMessage("Write some feedback or comment on at least one line.");
        RuleForEach(request => request.Dto.LineComments).ChildRules(comment =>
        {
            comment.RuleFor(c => c.Path).NotEmpty().MaximumLength(1000);
            comment.RuleFor(c => c.Line).GreaterThan(0);
            comment.RuleFor(c => c.Side).IsInEnum();
            comment.RuleFor(c => c.Body).NotEmpty().MaximumLength(PullRequestFeedback.MaxBodyLength);
        });
    }
}
