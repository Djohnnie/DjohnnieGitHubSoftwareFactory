using Djohnnie.SoftwareFactory.Agents;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalAgents;

internal static class InternalAgentKeyRules
{
    extension<T>(IRuleBuilder<T, string> rule)
    {
        public IRuleBuilderOptions<T, string> IsInternalAgentKey()
        {
            return rule.NotEmpty().Must(key => InternalAgentDefaults.Find(key) is not null).WithMessage("Unknown internal agent.");
        }
    }
}
