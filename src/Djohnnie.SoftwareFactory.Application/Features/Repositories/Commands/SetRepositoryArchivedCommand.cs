using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Repositories;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Repositories;

/// <summary>Marks a known repository as removed from GitHub, or as back on it; the response says whether anything changed.</summary>
public sealed record SetRepositoryArchivedCommand(SetRepositoryArchivedRequest Dto) : IRequest<Response<SetRepositoryArchivedResult>>;
