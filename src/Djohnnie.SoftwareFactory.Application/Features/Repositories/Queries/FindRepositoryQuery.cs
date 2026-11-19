using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.Repositories;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Repositories;

/// <summary>The repository as the database knows it; <c>null</c> when it doesn't.</summary>
public sealed record FindRepositoryQuery(RepositoryRequest Dto) : IRequest<Response<RepositorySummary?>>;
