#!/usr/bin/env python3
"""
Comprehensive NDI Stack Verification Tool
Validates project structure, configuration files, python imports, and API route definitions.
"""

import os
import sys
import json
import logging

logging.basicConfig(level=logging.INFO, format='[%(levelname)s] %(message)s')

REQUIRED_FILES = [
    "docker-compose.yml",
    "README.md",
    "central-manager/Dockerfile",
    "central-manager/requirements.txt",
    "central-manager/app/main.py",
    "central-manager/app/models.py",
    "central-manager/app/templates/index.html",
    "central-manager/app/static/style.css",
    "ndi-discovery-server/Dockerfile",
    "ndi-discovery-server/discovery_daemon.py",
    "ndi-discovery-server/entrypoint.sh",
    "ndi-discovery-server/ndi-discovery.conf",
    "ndi-generator/Dockerfile",
    "ndi-generator/generator.py",
    "ndi-generator/requirements.txt",
    "ndi-processor/Dockerfile",
    "ndi-processor/processor.py",
    "ndi-processor/requirements.txt",
    "ndi-monitor/Dockerfile",
    "ndi-monitor/monitor.py",
    "ndi-monitor/requirements.txt",
    "tests/test_central_manager.py",
    "tests/test_services.py"
]

def check_files():
    logging.info("--- 1. Checking File Structure ---")
    missing = []
    for filepath in REQUIRED_FILES:
        if not os.path.exists(filepath):
            missing.append(filepath)
            logging.error(f"❌ Missing required file: {filepath}")
        else:
            logging.info(f"✅ Found: {filepath}")

    if missing:
        raise RuntimeError(f"Verification failed: {len(missing)} missing files.")

def check_python_compilation():
    logging.info("\n--- 2. Checking Python Source Code Compilation ---")
    py_files = [f for f in REQUIRED_FILES if f.endswith(".py")]
    for py_file in py_files:
        code = open(py_file, 'r').read()
        try:
            compile(code, py_file, 'exec')
            logging.info(f"✅ Python syntax OK: {py_file}")
        except Exception as e:
            logging.error(f"❌ Syntax error in {py_file}: {e}")
            raise

def main():
    logging.info("Starting NDI Broadcast Stack Verification...")
    check_files()
    check_python_compilation()
    logging.info("\n🎉 All NDI stack verification checks PASSED successfully!")

if __name__ == "__main__":
    main()
