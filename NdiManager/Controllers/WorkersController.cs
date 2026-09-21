using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NdiManager.Models;
using NdiManager.Services;

namespace NdiManager.Controllers
{
    [ApiController]
    [Route("api/workers")]
    public class WorkersController : ControllerBase
    {
        private readonly INdiWorkerManager _manager;

        public WorkersController(INdiWorkerManager manager)
        {
            _manager = manager;
        }

        [HttpGet]
        public async Task<ActionResult<List<WorkerResponse>>> ListWorkers()
        {
            var workers = await _manager.ListWorkersAsync();
            return Ok(workers);
        }

        [HttpPost]
        public async Task<ActionResult<WorkerResponse>> CreateWorker([FromBody] WorkerConfig config)
        {
            var worker = await _manager.CreateWorkerAsync(config);
            return CreatedAtAction(nameof(GetWorker), new { id = worker.Id }, worker);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<WorkerResponse>> GetWorker(string id)
        {
            var worker = await _manager.GetWorkerAsync(id);
            if (worker == null) return NotFound(new { detail = "Worker not found" });
            return Ok(worker);
        }

        [HttpPost("{id}/start")]
        public async Task<ActionResult<WorkerActionResponse>> StartWorker(string id)
        {
            bool ok = await _manager.StartWorkerAsync(id);
            if (!ok) return BadRequest(new { detail = "Failed to start worker" });
            return Ok(new WorkerActionResponse { Success = true, Message = "Worker started", WorkerId = id });
        }

        [HttpPost("{id}/stop")]
        public async Task<ActionResult<WorkerActionResponse>> StopWorker(string id)
        {
            bool ok = await _manager.StopWorkerAsync(id);
            if (!ok) return BadRequest(new { detail = "Failed to stop worker" });
            return Ok(new WorkerActionResponse { Success = true, Message = "Worker stopped", WorkerId = id });
        }

        [HttpPost("{id}/restart")]
        public async Task<ActionResult<WorkerActionResponse>> RestartWorker(string id)
        {
            bool ok = await _manager.RestartWorkerAsync(id);
            if (!ok) return BadRequest(new { detail = "Failed to restart worker" });
            return Ok(new WorkerActionResponse { Success = true, Message = "Worker restarted", WorkerId = id });
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult<WorkerActionResponse>> DeleteWorker(string id)
        {
            bool ok = await _manager.DeleteWorkerAsync(id);
            if (!ok) return BadRequest(new { detail = "Failed to delete worker" });
            return Ok(new WorkerActionResponse { Success = true, Message = "Worker deleted", WorkerId = id });
        }

        [HttpGet("{id}/logs")]
        public async Task<ActionResult> GetWorkerLogs(string id, [FromQuery] int lines = 100)
        {
            string logs = await _manager.GetWorkerLogsAsync(id, lines);
            return Ok(new { worker_id = id, logs = logs });
        }
    }
}
