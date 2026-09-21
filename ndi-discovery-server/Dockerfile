FROM python:3.11-slim

WORKDIR /app

# Copy configuration and script files
COPY ndi-discovery.conf /etc/ndi/ndi-discovery.conf
COPY discovery_daemon.py /usr/local/bin/discovery_daemon.py
COPY entrypoint.sh /entrypoint.sh

RUN chmod +x /entrypoint.sh /usr/local/bin/discovery_daemon.py

EXPOSE 5959/tcp 5959/udp

ENTRYPOINT ["/entrypoint.sh"]
