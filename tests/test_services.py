import unittest
import sys
import os
import importlib.util

class TestNDIServices(unittest.TestCase):

    def test_processor_endpoints(self):
        proc_spec = importlib.util.spec_from_file_location("processor", os.path.abspath("ndi-processor/processor.py"))
        proc_module = importlib.util.module_from_spec(proc_spec)
        proc_spec.loader.exec_module(proc_module)

        from fastapi.testclient import TestClient
        client = TestClient(proc_module.app)

        res = client.get("/health")
        self.assertEqual(res.status_code, 200)
        self.assertEqual(res.json()["status"], "ok")

        preview_res = client.get("/preview.mjpeg")
        self.assertEqual(preview_res.status_code, 200)
        self.assertEqual(preview_res.headers["content-type"], "image/jpeg")

    def test_monitor_endpoints(self):
        mon_spec = importlib.util.spec_from_file_location("monitor", os.path.abspath("ndi-monitor/monitor.py"))
        mon_module = importlib.util.module_from_spec(mon_spec)
        mon_spec.loader.exec_module(mon_module)

        from fastapi.testclient import TestClient
        client = TestClient(mon_module.app)

        res = client.get("/health")
        self.assertEqual(res.status_code, 200)
        self.assertEqual(res.json()["status"], "ok")

        metrics_res = client.get("/api/v1/metrics")
        self.assertEqual(metrics_res.status_code, 200)
        self.assertIn("monitored_streams", metrics_res.json())

if __name__ == "__main__":
    unittest.main()
