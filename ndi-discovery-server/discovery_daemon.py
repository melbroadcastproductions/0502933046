#!/usr/bin/env python3
import sys
import socket
import time
import signal
import json
import logging

logging.basicConfig(level=logging.INFO, format='%(asctime)s [%(levelname)s] %(message)s')

PORT = 5959
registered_sources = {}

def handle_client(client_socket, client_address):
    try:
        data = client_socket.recv(4096)
        if not data:
            return

        request_str = data.decode('utf-8', errors='ignore').strip()
        logging.info(f"Received request from {client_address}: {request_str}")

        if request_str.startswith("REGISTER"):
            # Format: REGISTER <source_name> <ip>:<port>
            parts = request_str.split(" ")
            if len(parts) >= 3:
                src_name = parts[1]
                src_addr = parts[2]
                registered_sources[src_name] = {"address": src_addr, "timestamp": time.time()}
                response = f"OK REGISTERED {src_name}\n"
            else:
                response = "ERROR INVALID_REGISTER_FORMAT\n"
        elif request_str.startswith("QUERY") or request_str.startswith("LIST"):
            # Return list of active NDI sources
            active = {k: v["address"] for k, v in registered_sources.items() if time.time() - v["timestamp"] < 300}
            response = "SOURCES " + json.dumps(active) + "\n"
        elif request_str.startswith("PING"):
            response = "PONG NDI-DISCOVERY-SERVER\n"
        else:
            response = "OK NDI-DISCOVERY-ACK\n"

        client_socket.sendall(response.encode('utf-8'))
    except Exception as e:
        logging.error(f"Error handling client {client_address}: {e}")
    finally:
        client_socket.close()

def main():
    server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server.bind(('0.0.0.0', PORT))
    server.listen(128)
    logging.info(f"NDI Discovery Server running on port {PORT}...")

    def signal_handler(sig, frame):
        logging.info("Shutting down NDI Discovery Server...")
        server.close()
        sys.exit(0)

    signal.signal(signal.SIGINT, signal_handler)
    signal.signal(signal.SIGTERM, signal_handler)

    while True:
        try:
            client_sock, client_addr = server.accept()
            handle_client(client_sock, client_addr)
        except Exception as e:
            logging.error(f"Error accepting connection: {e}")

if __name__ == "__main__":
    main()
