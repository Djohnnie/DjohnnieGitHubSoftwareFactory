namespace Djohnnie.SoftwareFactory.DataAccess.Sql;

internal static class ContractMapping
{
    public static Djohnnie.SoftwareFactory.Contracts.EpicTemplates.EpicTemplate ToContract(this Entities.EpicTemplate value) => new()
    {
        Id = value.Id,
        Name = value.Name,
        TitleTemplate = value.TitleTemplate,
        Description = value.Description,
        CreatedAt = value.CreatedAt,
        UpdatedAt = value.UpdatedAt
    };

    public static Djohnnie.SoftwareFactory.Contracts.AiModels.AiModel ToContract(this Entities.AiModel value) => new()
    {
        Id = value.Id,
        Name = value.Name,
        ModelId = value.ModelId,
        Provider = value.Provider,
        ReleaseStatus = value.ReleaseStatus,
        Category = value.Category,
        Tier = value.Tier,
        Threshold = value.Threshold,
        InputPrice = value.InputPrice,
        CachedInputPrice = value.CachedInputPrice,
        CacheWritePrice = value.CacheWritePrice,
        OutputPrice = value.OutputPrice,
        IsEnabled = value.IsEnabled,
        SourceUrl = value.SourceUrl,
        UpdatedAt = value.UpdatedAt
    };

    public static Entities.AiModel ToEntity(this Djohnnie.SoftwareFactory.Contracts.AiModels.AiModel value) => new()
    {
        Id = value.Id,
        Name = value.Name,
        ModelId = value.ModelId,
        Provider = value.Provider,
        ReleaseStatus = value.ReleaseStatus,
        Category = value.Category,
        Tier = value.Tier,
        Threshold = value.Threshold,
        InputPrice = value.InputPrice,
        CachedInputPrice = value.CachedInputPrice,
        CacheWritePrice = value.CacheWritePrice,
        OutputPrice = value.OutputPrice,
        IsEnabled = value.IsEnabled,
        SourceUrl = value.SourceUrl,
        UpdatedAt = value.UpdatedAt
    };

    public static Djohnnie.SoftwareFactory.Contracts.Epics.Epic ToContract(this Entities.Epic value) => new()
    {
        Id = value.Id,
        RepositoryFullName = value.RepositoryFullName,
        Title = value.Title,
        Description = value.Description,
        Status = value.Status,
        CustomAgent = value.CustomAgent,
        Model = value.Model,
        BaseBranch = value.BaseBranch,
        CustomInstructions = value.CustomInstructions,
        IssueNumber = value.IssueNumber,
        IssueUrl = value.IssueUrl,
        CreatedAt = value.CreatedAt,
        UpdatedAt = value.UpdatedAt,
        AcceptedAt = value.AcceptedAt,
        AssignedAt = value.AssignedAt
    };

    public static Entities.Epic ToEntity(this Djohnnie.SoftwareFactory.Contracts.Epics.Epic value) => new()
    {
        Id = value.Id,
        RepositoryFullName = value.RepositoryFullName,
        Title = value.Title,
        Description = value.Description,
        Status = value.Status,
        CustomAgent = value.CustomAgent,
        Model = value.Model,
        BaseBranch = value.BaseBranch,
        CustomInstructions = value.CustomInstructions,
        IssueNumber = value.IssueNumber,
        IssueUrl = value.IssueUrl,
        CreatedAt = value.CreatedAt,
        UpdatedAt = value.UpdatedAt,
        AcceptedAt = value.AcceptedAt,
        AssignedAt = value.AssignedAt
    };

    public static Djohnnie.SoftwareFactory.Contracts.AgentTemplates.AgentTemplate ToContract(this Entities.AgentTemplate value) => new()
    {
        Id = value.Id,
        FileName = value.FileName,
        Name = value.Name,
        Description = value.Description,
        Content = value.Content,
        CreatedAt = value.CreatedAt,
        UpdatedAt = value.UpdatedAt
    };

    public static Entities.AgentTemplate ToEntity(this Djohnnie.SoftwareFactory.Contracts.AgentTemplates.AgentTemplate value) => new()
    {
        Id = value.Id,
        FileName = value.FileName,
        Name = value.Name,
        Description = value.Description,
        Content = value.Content,
        CreatedAt = value.CreatedAt,
        UpdatedAt = value.UpdatedAt
    };

}
