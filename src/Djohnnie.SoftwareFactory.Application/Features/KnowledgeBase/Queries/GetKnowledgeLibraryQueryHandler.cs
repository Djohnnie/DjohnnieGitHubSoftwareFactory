using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.KnowledgeBase;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.KnowledgeBase;

public sealed class GetKnowledgeLibraryQueryHandler(IKnowledgeBaseStore store, IKnowledgeFileStorage storage) : IRequestHandler<GetKnowledgeLibraryQuery, Response<KnowledgeLibrary>>
{
    public async Task<Response<KnowledgeLibrary>> Handle(GetKnowledgeLibraryQuery request, CancellationToken cancellationToken)
    {
        var library = await store.GetLibraryAsync(request.Dto.RepositoryFullName, cancellationToken);
        return new(library with { Files = library.Files.Select(file => file with { Url = storage.GetUrl(file.Id) }).ToList() });
    }
}
