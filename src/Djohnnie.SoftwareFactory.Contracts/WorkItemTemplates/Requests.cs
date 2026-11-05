using Djohnnie.SoftwareFactory.Contracts.WorkItems;

namespace Djohnnie.SoftwareFactory.Contracts.WorkItemTemplates;

public sealed record SaveWorkItemTemplateRequest(WorkItemTemplate Template);
public sealed record WorkItemTemplatesRequest(WorkItemType Type);
