using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.AiModels;
using Djohnnie.SoftwareFactory.Contracts.Common;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AiModels;

public sealed class ScrapeModelsQueryHandler(ModelPricingScraper scraper) : IRequestHandler<ScrapeModelsQuery, Response<CollectionResult<AiModel>>>
{
    public async Task<Response<CollectionResult<AiModel>>> Handle(ScrapeModelsQuery request, CancellationToken cancellationToken)
    {
        return new(new(await scraper.ScrapeAsync(request.Dto.Url, cancellationToken)));
    }
}
