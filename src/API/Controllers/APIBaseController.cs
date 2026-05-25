using Microsoft.AspNetCore.Mvc;

namespace IA.API.Controllers
{
    [Produces("application/json")]
    [Authorize("APIAuth")]
    public abstract class APIBaseController : ControllerBase
    {

        protected APIBaseController()
        {
           
        }
         
    }

}
