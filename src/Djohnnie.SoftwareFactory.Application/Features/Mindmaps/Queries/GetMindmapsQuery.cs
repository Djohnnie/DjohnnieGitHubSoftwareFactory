using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.Mindmaps;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Mindmaps;

public sealed record GetMindmapsQuery(RepositoryRequest Dto) : IRequest<Response<CollectionResult<MindmapSummary>>>;
