using Djohnnie.SoftwareFactory.Application.Features.Agents;
using Djohnnie.SoftwareFactory.Contracts.Agents;
using Djohnnie.SoftwareFactory.Contracts.AgentTemplates;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentTemplates;

public static class AgentTemplateExtensions
{
    extension(AgentTemplate template)
    {
        public AgentDefinition ToAgent()
        {
            return AgentFileSerializer.Parse(template.Content);
        }
    }
}
