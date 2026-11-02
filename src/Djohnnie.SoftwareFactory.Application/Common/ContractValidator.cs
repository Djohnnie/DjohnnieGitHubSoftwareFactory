using System.ComponentModel.DataAnnotations;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Common;

public sealed class ContractValidator<T> : AbstractValidator<T> where T : class
{
    public ContractValidator()
    {
        RuleFor(value => value).Custom((value, context) =>
        {
            var results = new List<ValidationResult>();
            if (!System.ComponentModel.DataAnnotations.Validator.TryValidateObject(value, new(value), results, validateAllProperties: true))
            {
                foreach (var result in results)
                {
                    context.AddFailure(string.Join(".", result.MemberNames), result.ErrorMessage ?? "Invalid value.");
                }
            }
        });
    }
}
