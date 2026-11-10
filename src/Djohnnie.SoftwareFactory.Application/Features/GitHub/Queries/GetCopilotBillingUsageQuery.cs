using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

/// <summary>This month's premium request or AI credit usage of the account that owns the repository.</summary>
public sealed record GetCopilotBillingUsageQuery(CopilotBillingUsageRequest Dto) : IRequest<Response<CopilotBillingUsage>>;
