using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.KnowledgeBase;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.KnowledgeBase;

public sealed record SearchKnowledgeBaseQuery(SearchKnowledgeBaseRequest Dto) : IRequest<Response<CollectionResult<KnowledgeSearchResult>>>;
