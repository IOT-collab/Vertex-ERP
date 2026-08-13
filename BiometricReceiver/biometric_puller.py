import os
import re
import socket
import subprocess
import sys
import time
import urllib.request
from datetime import datetime, timezone


BASE_DIRECTORY = os.path.dirname(os.path.abspath(__file__))
for package_directory in (
    os.path.join(BASE_DIRECTORY, "packages"),
    os.path.join(os.path.dirname(BASE_DIRECTORY), "BiometricPuller", "packages"),
):
    if os.path.isdir(package_directory):
        sys.path.insert(0, package_directory)

from zk import ZK


SERIAL_NUMBER = "GED7252300347"
SERVER_URL = f"http://127.0.0.1:8082/iclock/cdata?SN={SERIAL_NUMBER}&table=ATTLOG"
KNOWN_DEVICE_IPS = {"192.168.0.179", "192.168.0.163"}
KNOWN_MAC_ADDRESSES = {"10-a5-62-6d-79-c4", "00-17-61-11-2a-f6"}
LOG_DIRECTORY = os.path.join(BASE_DIRECTORY, "logs")
LOG_PATH = os.path.join(LOG_DIRECTORY, "direct-puller.log")


def log(message):
    os.makedirs(LOG_DIRECTORY, exist_ok=True)
    with open(LOG_PATH, "a", encoding="utf-8") as stream:
        stream.write(f"{datetime.now(timezone.utc).isoformat()}\t{message}\n")


def device_ips():
    addresses = set(KNOWN_DEVICE_IPS)
    try:
        output = subprocess.check_output(["arp", "-a"], text=True, errors="ignore")
        for line in output.splitlines():
            match = re.search(r"(192\.168\.0\.\d+)\s+([0-9a-f-]{17})", line, re.IGNORECASE)
            if match and match.group(2).lower() in KNOWN_MAC_ADDRESSES:
                addresses.add(match.group(1))
    except Exception:
        pass
    return sorted(addresses, reverse=True)


def upload(attendance):
    rows = []
    for item in attendance:
        rows.append(
            f"{str(item.user_id).strip()}\t{item.timestamp:%Y-%m-%d %H:%M:%S}\t"
            f"{item.status}\t{item.punch}\t0"
        )
    if not rows:
        return "OK: 0"
    request = urllib.request.Request(
        SERVER_URL,
        data="\n".join(rows).encode("utf-8"),
        headers={"Content-Type": "text/plain"},
        method="POST",
    )
    with urllib.request.urlopen(request, timeout=15) as response:
        return response.read().decode("utf-8", errors="replace").strip()


def try_pull(ip_address):
    device = ZK(
        ip_address,
        port=4370,
        timeout=4,
        password=0,
        force_udp=True,
        ommit_ping=True,
    )
    connection = None
    try:
        connection = device.connect()
        attendance = connection.get_attendance()
        result = upload(attendance)
        log(f"CONNECTED ip={ip_address} records={len(attendance)} response={result}")
        return True
    finally:
        if connection is not None:
            try:
                connection.disconnect()
            except Exception:
                pass


def acquire_single_instance():
    lock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    try:
        lock.bind(("127.0.0.1", 43701))
        lock.listen(1)
        return lock
    except OSError:
        return None


def main():
    instance_lock = acquire_single_instance()
    if instance_lock is None:
        return
    log("Direct biometric puller started.")
    last_error_log = 0.0
    while True:
        connected = False
        for ip_address in device_ips():
            try:
                if try_pull(ip_address):
                    connected = True
                    break
            except Exception as exception:
                if time.monotonic() - last_error_log >= 300:
                    log(f"WAITING ip={ip_address} error={type(exception).__name__}: {exception}")
                    last_error_log = time.monotonic()
        time.sleep(10 if connected else 15)


if __name__ == "__main__":
    main()
