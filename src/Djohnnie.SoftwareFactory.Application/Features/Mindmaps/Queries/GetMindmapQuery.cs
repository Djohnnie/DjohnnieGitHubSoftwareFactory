using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.Mindmaps;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Mindmaps;

public sealed record GetMindmapQuery(RepositoryItemRequest Dto) : IRequest<Response<Mindmap?>>;
