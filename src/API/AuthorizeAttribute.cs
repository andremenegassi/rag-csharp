using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace IA.API
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class AuthorizeAttribute : Attribute, IAuthorizationFilter
    {
        private string PolicyName { get; set; }

        public AuthorizeAttribute(string policyName) :base()
        {
            PolicyName = policyName;
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            if (PolicyName != "APIAuth")
                return;

            var env = context.HttpContext.RequestServices.GetService<IWebHostEnvironment>();
            if (env.IsDevelopment())
                return;

            var authorization = context.HttpContext.Request.Headers["Authorization"];

            if (!string.IsNullOrEmpty(authorization))
                authorization = authorization.ToString().Replace("Bearer ", "");

            string[] apiKey = ObterAPIKey(context);

            if (string.IsNullOrEmpty(authorization) || !apiKey.Contains(authorization.ToString()))
            {
                context.Result = new JsonResult(new { message = "Unauthorized" }) { StatusCode = StatusCodes.Status401Unauthorized };
            }
            
        }

        private string[] ObterAPIKey(AuthorizationFilterContext context)
        {
            var config = context.HttpContext.RequestServices.GetService<IAppSettings>();
            return config.APIKeys;
        }
  
    }
}
