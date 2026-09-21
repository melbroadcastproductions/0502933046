using System;
using System.Threading.Tasks;
using Xunit;
using NdiManager.Models;
using NdiManager.Services;
using NdiManager.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace NdiManager.Tests
{
    public class ManagerTests
    {
        [Fact]
        public async Task SystemStatus_ReturnsValidResponse()
        {
            var manager = new NdiWorkerManager();
            var status = await manager.GetSystemStatusAsync();

            Assert.NotNull(status);
            Assert.True(status.MemoryUsageMb >= 0);
        }

        [Fact]
        public async Task WorkerLifecycle_CreateListStopDelete_Succeeds()
        {
            var manager = new NdiWorkerManager();
            var config = new WorkerConfig
            {
                StreamName = "TEST-STREAM-01",
                SourceType = "WebPage",
                SourceUri = "https://finance.yahoo.com",
                Resolution = "1280x720",
                Fps = "30",
                TallyState = "Program",
                UmdText = "CAM 1"
            };

            // 1. Create worker
            var created = await manager.CreateWorkerAsync(config);
            Assert.NotNull(created);
            Assert.NotEmpty(created.Id);
            Assert.Equal("TEST-STREAM-01", created.Config.StreamName);
            Assert.Equal("WebPage", created.Config.SourceType);

            // 2. List workers
            var list = await manager.ListWorkersAsync();
            Assert.Contains(list, w => w.Id == created.Id);

            // 3. Get worker by ID
            var fetched = await manager.GetWorkerAsync(created.Id);
            Assert.NotNull(fetched);
            Assert.Equal(created.Id, fetched.Id);

            // 4. Stop worker
            bool stopped = await manager.StopWorkerAsync(created.Id);
            Assert.True(stopped);

            // 5. Delete worker
            bool deleted = await manager.DeleteWorkerAsync(created.Id);
            Assert.True(deleted);
        }

        [Fact]
        public async Task WorkersController_Endpoints_ReturnOk()
        {
            var manager = new NdiWorkerManager();
            var controller = new WorkersController(manager);

            var result = await controller.ListWorkers();
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public async Task Worker_QuadSplitConfig_Succeeds()
        {
            var manager = new NdiWorkerManager();
            var config = new WorkerConfig
            {
                StreamName = "QUADSPLIT-01",
                SourceType = "QuadSplit",
                SourceUri = "udp://239.255.0.1:5001,udp://239.255.0.1:5002,udp://239.255.0.1:5003,udp://239.255.0.1:5004",
                Resolution = "1920x1080",
                Fps = "30"
            };

            var created = await manager.CreateWorkerAsync(config);
            Assert.NotNull(created);
            Assert.Equal("QuadSplit", created.Config.SourceType);
            Assert.Contains("5001", created.Config.SourceUri);

            await manager.DeleteWorkerAsync(created.Id);
        }

        [Fact]
        public async Task TallyController_SetTally_UpdatesWorkerTallyState()
        {
            var manager = new NdiWorkerManager();
            var config = new WorkerConfig { StreamName = "TALLY-TEST", TallyState = "Off" };
            var created = await manager.CreateWorkerAsync(config);

            var tallyController = new TallyController(manager);
            var req = new TallyRequest { TallyState = "Preview" };

            var actionResult = await tallyController.SetTally(created.Id, req);
            Assert.IsType<OkObjectResult>(actionResult);

            var updated = await manager.GetWorkerAsync(created.Id);
            Assert.NotNull(updated);
            Assert.Equal("Preview", updated.Config.TallyState);

            await manager.DeleteWorkerAsync(created.Id);
        }
    }
}
