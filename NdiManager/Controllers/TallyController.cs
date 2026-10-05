using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NdiManager.Services;

namespace NdiManager.Controllers
{
    public class TallyRequest
    {
        public string TallyState { get; set; } = "Off"; // Off, Program, Preview
    }

    [ApiController]
    [Route("api/tally")]
    public class TallyController : ControllerBase
    {
        private readonly INdiWorkerManager _manager;

        public TallyController(INdiWorkerManager manager)
        {
            _manager = manager;
        }

        [HttpPost("{workerId}")]
        public async Task<IActionResult> SetTally(string workerId, [FromBody] TallyRequest request)
        {
            var worker = await _manager.GetWorkerAsync(workerId);
            if (worker == null) return NotFound(new { detail = "Worker not found" });

            worker.Config.TallyState = request.TallyState;
            return Ok(new { success = true, worker_id = workerId, tally_state = request.TallyState });
        }
    }
}
