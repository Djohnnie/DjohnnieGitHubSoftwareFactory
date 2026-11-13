using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Mindmaps;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Mindmaps;

/// <summary>Links an idea of a stored mindmap to a work item (an idea of a closed item is marked done), or unlinks it.</summary>
public sealed record LinkMindmapIdeaCommand(LinkMindmapIdeaRequest Dto) : IRequest<Response<Mindmap>>;
