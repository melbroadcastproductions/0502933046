using System.Collections.Generic;
using System.Threading.Tasks;
using NdiManager.Models;

namespace NdiManager.Services
{
    public interface INdiWorkerManager
    {
        Task<List<WorkerResponse>> ListWorkersAsync();
        Task<WorkerResponse?> GetWorkerAsync(string workerId);
        Task<WorkerResponse> CreateWorkerAsync(WorkerConfig config);
        Task<bool> StartWorkerAsync(string workerId);
        Task<bool> StopWorkerAsync(string workerId);
        Task<bool> RestartWorkerAsync(string workerId);
        Task<bool> DeleteWorkerAsync(string workerId);
        Task<string> GetWorkerLogsAsync(string workerId, int lines = 100);
        Task<SystemStatusResponse> GetSystemStatusAsync();
    }
}
