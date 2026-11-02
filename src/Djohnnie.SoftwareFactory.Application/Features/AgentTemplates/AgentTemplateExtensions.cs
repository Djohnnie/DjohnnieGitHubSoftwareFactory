using Djohnnie.SoftwareFactory.Application.Features.Agents;
using Djohnnie.SoftwareFactory.Contracts.Agents;
using Djohnnie.SoftwareFactory.Contracts.AgentTemplates;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentTemplates;

public static class AgentTemplateExtensions
{
    public static AgentDefinition ToAgent(this AgentTemplate template) => AgentFileSerializer.Parse(template.Content);
}
