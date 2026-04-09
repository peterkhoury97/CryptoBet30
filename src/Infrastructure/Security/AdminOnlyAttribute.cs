using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CryptoBet30.Infrastructure.Security;

/// <summary>
/// Authorization attribute that requires admin role
/// </summary>
public class AdminOnlyAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        
        if (!user.Identity?.IsAuthenticated ?? true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var isAdmin = user.Claims.FirstOrDefault(c => c.Type == "is_admin")?.Value == "true";
        
        if (!isAdmin)
        {
            context.Result = new ForbidResult();
        }
    }
}
