using Djohnnie.SoftwareFactory.Contracts.KnowledgeBase;
using MediatR;
using Microsoft.Extensions.Options;

namespace Djohnnie.SoftwareFactory.Application.Features.KnowledgeBase;

/// <summary>The knowledge base: files in virtual folders, shared across repositories and linkable from work items.</summary>
public sealed class KnowledgeBaseService(ISender mediator, IOptions<KnowledgeBaseOptions> options, IOptions<KnowledgeSearchOptions> searchOptions)
{
    /// <summary>Whether a file storage is configured; otherwise files can't be uploaded.</summary>
    public bool IsEnabled => options.Value.IsConfigured;

    /// <summary>Whether the Azure AI Search index is configured; otherwise the chat agents can't search the files.</summary>
    public bool IsSearchEnabled => searchOptions.Value.IsConfigured;

    public long MaxFileSize => options.Value.MaxFileSize;

    public int MaxFileSizeInMegabytes => options.Value.MaxFileSizeInMegabytes;

    /// <summary>The folders, the global files and the files of the repository (only the global files with <c>null</c>).</summary>
    public async Task<KnowledgeLibrary> GetLibraryAsync(string? repositoryFullName, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetKnowledgeLibraryQuery(new(repositoryFullName)), cancellationToken)).Dto;
    }

    /// <summary>The passages of the global files and the repository's files that best match the query, with their file.</summary>
    public async Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(string repositoryFullName, string query, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new SearchKnowledgeBaseQuery(new(repositoryFullName, query)), cancellationToken)).Dto.Items;
    }

    /// <summary>Creates (empty id) or renames and moves a folder; the name is trimmed.</summary>
    public async Task<KnowledgeFolder> SaveFolderAsync(KnowledgeFolder folder, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new SaveKnowledgeFolderCommand(new(folder)), cancellationToken)).Dto;
    }

    /// <summary>Deletes an empty folder of the repository.</summary>
    public async Task DeleteFolderAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default)
    {
        await mediator.Send(new DeleteKnowledgeFolderCommand(new(repositoryFullName, id)), cancellationToken);
    }

    /// <summary>Stores the content as a new blob and adds the file to the library.</summary>
    public async Task<KnowledgeFile> UploadFileAsync(UploadKnowledgeFileRequest request, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new UploadKnowledgeFileCommand(request), cancellationToken)).Dto;
    }

    /// <summary>Changes the title, description and folder of a file.</summary>
    public async Task<KnowledgeFile> SaveFileAsync(KnowledgeFile file, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new SaveKnowledgeFileCommand(new(file)), cancellationToken)).Dto;
    }

    /// <summary>Removes the file and its blob; links to it stop working.</summary>
    public async Task DeleteFileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await mediator.Send(new DeleteKnowledgeFileCommand(new(id)), cancellationToken);
    }
}
