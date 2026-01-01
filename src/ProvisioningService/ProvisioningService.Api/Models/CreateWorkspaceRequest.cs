using System.ComponentModel.DataAnnotations;

namespace ProvisioningService.Api.Models;

public record CreateWorkspaceRequest : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        yield break;
    }
}
