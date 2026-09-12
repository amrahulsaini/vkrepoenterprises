"""Run a local-only candidate with the mobile service's existing environment.

Use from a transient systemd unit, with its working directory set to the
candidate publish folder and User=www-data. Stop the unit after verification.
"""
import os
import subprocess

pid = subprocess.check_output(["systemctl", "show", "vkmobileapi", "-p", "MainPID", "--value"], text=True).strip()
with open(f"/proc/{pid}/environ", "rb") as source:
    env = dict(part.decode().split("=", 1) for part in source.read().split(b"\0") if b"=" in part)
os.execve("/usr/bin/dotnet", ["dotnet", "VKmobileapi.dll", "--urls", "http://127.0.0.1:5003"], env)
