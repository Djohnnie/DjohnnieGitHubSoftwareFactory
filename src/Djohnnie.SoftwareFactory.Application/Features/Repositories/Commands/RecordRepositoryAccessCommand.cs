using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Repositories;

public sealed record RecordRepositoryAccessCommand(RepositoryRequest Dto) : IRequest<Response<OperationResult>>;
