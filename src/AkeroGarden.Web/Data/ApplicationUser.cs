using Microsoft.AspNetCore.Identity;

namespace AkeroGarden.Web.Data;

public class ApplicationUser : IdentityUser
{
    public bool IsActive { get; set; } = true;
}
