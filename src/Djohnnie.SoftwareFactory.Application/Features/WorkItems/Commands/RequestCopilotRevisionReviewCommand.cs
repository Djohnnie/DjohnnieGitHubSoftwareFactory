using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

/// <summary>
/// Asks Copilot code review to review the item's pull request once the Copilot cloud agent finished the revision that feedback
/// sent from DGHSF asked for; see <see cref="CopilotRevisionReviews"/>.
/// </summary>
public sealed record RequestCopilotRevisionReviewCommand(RepositoryItemRequest Dto) : IRequest<Response<CopilotRevisionReviewResult>>;
