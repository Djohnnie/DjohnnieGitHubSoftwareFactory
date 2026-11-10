using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed record SendPullRequestFeedbackCommand(PullRequestFeedback Dto) : IRequest<Response<PullRequestFeedbackResult>>;
