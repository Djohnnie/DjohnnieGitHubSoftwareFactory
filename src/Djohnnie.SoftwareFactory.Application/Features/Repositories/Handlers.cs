using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.Repositories;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Repositories;

public sealed class RepositoryHandlers(RepositoryStore store) :
    IRequestHandler<GetRecentRepositoriesQuery, Response<CollectionResult<RepositorySummary>>>,
    IRequestHandler<GetFavoriteRepositoriesQuery, Response<CollectionResult<RepositorySummary>>>,
    IRequestHandler<RecordRepositoryAccessCommand, Response<OperationResult>>,
    IRequestHandler<SetRepositoryFavoriteCommand, Response<OperationResult>>
{
    public async Task<Response<CollectionResult<RepositorySummary>>> Handle(GetRecentRepositoriesQuery request, CancellationToken cancellationToken) =>
        new(new(await store.GetRecentAsync(cancellationToken)));

    public async Task<Response<CollectionResult<RepositorySummary>>> Handle(GetFavoriteRepositoriesQuery request, CancellationToken cancellationToken) =>
        new(new(await store.GetFavoritesAsync(cancellationToken)));

    public async Task<Response<OperationResult>> Handle(RecordRepositoryAccessCommand request, CancellationToken cancellationToken)
    {
        await store.RecordAccessAsync(request.Dto.RepositoryFullName, cancellationToken);
        return new(new());
    }

    public async Task<Response<OperationResult>> Handle(SetRepositoryFavoriteCommand request, CancellationToken cancellationToken)
    {
        await store.SetFavoriteAsync(request.Dto.RepositoryFullName, request.Dto.IsFavorite, cancellationToken);
        return new(new());
    }
}
