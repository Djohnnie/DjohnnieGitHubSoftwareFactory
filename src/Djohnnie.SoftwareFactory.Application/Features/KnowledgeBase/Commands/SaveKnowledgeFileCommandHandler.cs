using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.KnowledgeBase;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.KnowledgeBase;

public sealed class SaveKnowledgeFileCommandHandler(IKnowledgeBaseStore store, IKnowledgeFileStorage storage, KnowledgeFileMetadataSync metadata)
    : IRequestHandler<SaveKnowledgeFileCommand, Response<KnowledgeFile>>
{
    public async Task<Response<KnowledgeFile>> Handle(SaveKnowledgeFileCommand request, CancellationToken cancellationToken)
    {
        var stored = await store.UpdateFileAsync(request.Dto.File, cancellationToken);
        // The library gives the (possibly new) folder's path for the blob metadata.
        await metadata.UpdateAsync([stored], await store.GetFullLibraryAsync(cancellationToken), cancellationToken);
        return new(stored with { Url = storage.GetUrl(stored.Id) });
    }
}
