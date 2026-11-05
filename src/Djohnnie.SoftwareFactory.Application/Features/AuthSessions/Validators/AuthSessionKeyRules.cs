using Djohnnie.SoftwareFactory.Contracts.AuthSessions;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.AuthSessions;

public static class AuthSessionKeyRules
{
    extension<T>(IRuleBuilder<T, string> rule)
    {
        public IRuleBuilderOptions<T, string> ValidAuthSessionKey()
        {
            return rule.NotEmpty().MaximumLength(AuthSession.MaxKeyLength);
        }
    }
}
