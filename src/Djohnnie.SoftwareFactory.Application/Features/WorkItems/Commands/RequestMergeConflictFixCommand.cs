using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

/// <summary>Asks the item's agent to fix the merge conflicts of its pull request now, as the signed-in user; see <see cref="MergeConflictFixes"/>.</summary>
public sealed record RequestMergeConflictFixCommand(RepositoryItemRequest Dto) : IRequest<Response<PullRequestFeedbackResult>>;
