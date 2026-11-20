using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

/// <summary>
/// Asks the item's agent to revise its pull request when Copilot code review has reviewed it with
/// remarks and the item asks for it (<see cref="WorkItem.ReviseOnCopilotReview"/>); see <see cref="CopilotReviewRevisions"/>.
/// </summary>
public sealed record StartCopilotReviewRevisionCommand(RepositoryItemRequest Dto) : IRequest<Response<CopilotReviewRevisionResult>>;
