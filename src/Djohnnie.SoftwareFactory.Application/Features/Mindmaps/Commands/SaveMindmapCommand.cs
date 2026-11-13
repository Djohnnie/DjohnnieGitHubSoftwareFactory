using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Mindmaps;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Mindmaps;

public sealed record SaveMindmapCommand(SaveMindmapRequest Dto) : IRequest<Response<Mindmap>>;
