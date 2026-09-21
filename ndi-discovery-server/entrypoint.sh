#!/bin/sh
set -e

echo "Starting NDI Discovery Server on port 5959..."
if [ -f "/etc/ndi/ndi-discovery.conf" ]; then
    echo "Loaded configuration from /etc/ndi/ndi-discovery.conf"
fi

exec python3 /usr/local/bin/discovery_daemon.py
