import unittest
import sys
import os

# Add central-manager path to sys.path
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), '../central-manager')))

from fastapi.testclient import TestClient
from app.main import app

client = TestClient(app)

class TestCentralManager(unittest.TestCase):

    def test_health_endpoint(self):
        response = client.get("/api/v1/health")
        self.assertEqual(response.status_code, 200)
        data = response.json()
        self.assertEqual(data["status"], "ok")
        self.assertEqual(data["service"], "central-manager")

    def test_discovery_info_endpoint(self):
        response = client.get("/api/v1/discovery")
        self.assertEqual(response.status_code, 200)
        data = response.json()
        self.assertIn("discovery_server", data)
        self.assertEqual(data["port"], 5959)

    def test_service_registration_and_lifecycle(self):
        # 1. Register a service
        reg_payload = {
            "service_id": "test-gen-99",
            "name": "Unit Test Generator",
            "service_type": "generator",
            "host": "127.0.0.1",
            "port": 8001,
            "ndi_sources": ["TEST_BARS_1080P"],
            "metadata": {"pattern": "SMPTE"}
        }
        res = client.post("/api/v1/services/register", json=reg_payload)
        self.assertEqual(res.status_code, 201)

        # 2. List services and verify
        res = client.get("/api/v1/services")
        self.assertEqual(res.status_code, 200)
        services = res.json()
        self.assertTrue(any(s["service_id"] == "test-gen-99" for s in services))

        # 3. Send heartbeat
        hb_payload = {
            "status": "healthy",
            "metrics": {"fps": 60.0},
            "active_sources": ["TEST_BARS_1080P"]
        }
        res = client.post("/api/v1/services/test-gen-99/heartbeat", json=hb_payload)
        self.assertEqual(res.status_code, 200)

        # 4. Get specific service
        res = client.get("/api/v1/services/test-gen-99")
        self.assertEqual(res.status_code, 200)
        self.assertEqual(res.json()["metrics"]["fps"], 60.0)

        # 5. Delete service
        res = client.delete("/api/v1/services/test-gen-99")
        self.assertEqual(res.status_code, 200)

        # 6. Verify service deleted
        res = client.get("/api/v1/services/test-gen-99")
        self.assertEqual(res.status_code, 404)

    def test_dashboard_route(self):
        response = client.get("/")
        self.assertEqual(response.status_code, 200)
        self.assertIn("text/html", response.headers["content-type"])

if __name__ == "__main__":
    unittest.main()
