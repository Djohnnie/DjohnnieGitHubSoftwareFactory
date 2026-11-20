using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

/// <summary>
/// Starts a revision run of the DGHSF agent worker when Copilot code review has reviewed the item's pull request with
/// remarks and the item asks for it (<see cref="WorkItem.ReviseOnCopilotReview"/>); see <see cref="CopilotReviewRevisions"/>.
/// </summary>
public sealed record StartCopilotReviewRevisionCommand(RepositoryItemRequest Dto) : IRequest<Response<CopilotReviewRevisionResult>>;
