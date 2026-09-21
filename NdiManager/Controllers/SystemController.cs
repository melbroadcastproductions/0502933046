using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NdiManager.Models;
using NdiManager.Services;

namespace NdiManager.Controllers
{
    [ApiController]
    [Route("api/system")]
    public class SystemController : ControllerBase
    {
        private readonly INdiWorkerManager _manager;

        public SystemController(INdiWorkerManager manager)
        {
            _manager = manager;
        }

        [HttpGet("status")]
        public async Task<ActionResult<SystemStatusResponse>> GetStatus()
        {
            var status = await _manager.GetSystemStatusAsync();
            return Ok(status);
        }
    }
}
