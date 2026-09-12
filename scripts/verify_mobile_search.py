"""Read-only mobile search regression checks against a running candidate API.

Run on the server with TENANT_DB_SECRET in the environment, or as an operator
with --service-env to read it from the running mobile service (never printed).
No registrations, messages, settings, or records are changed.
"""
import argparse
import base64
import hashlib
import hmac
import json
import os
import re
import statistics
import subprocess
import time
import urllib.parse
import urllib.request


def key(value):
    return re.sub(r"[^A-Z0-9]", "", (value or "").upper())


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", default="http://127.0.0.1:5001")
    parser.add_argument("--slug", default="v_k_enterprises")
    parser.add_argument("--user-id", type=int, required=True)
    parser.add_argument("--finance-id", type=int, action="append", default=[])
    parser.add_argument("--service-env", action="store_true")
    args = parser.parse_args()
    env = dict(os.environ)
    if args.service_env:
        pid = subprocess.check_output(["systemctl", "show", "vkmobileapi", "-p", "MainPID", "--value"], text=True).strip()
        with open(f"/proc/{pid}/environ", "rb") as source:
            env.update(part.decode().split("=", 1) for part in source.read().split(b"\0") if b"=" in part)
    payload = f"{args.slug}|{int(time.time()) + 600}|{args.user_id}|".encode()
    b64 = lambda value: base64.urlsafe_b64encode(value).decode().rstrip("=")
    token = "mt1." + b64(payload) + "." + b64(hmac.new(env["TENANT_DB_SECRET"].encode(), payload, hashlib.sha256).digest())
    timings = []

    def get(path, **query):
        url = args.base_url.rstrip("/") + "/api/mobile/" + path
        if query:
            url += "?" + urllib.parse.urlencode(query)
        request = urllib.request.Request(url, headers={"X-Tenant-Token": token, "X-User-Id": str(args.user_id)})
        started = time.perf_counter()
        with urllib.request.urlopen(request, timeout=25) as response:
            result = json.load(response)
        timings.append((time.perf_counter() - started) * 1000)
        return result

    all_rows = get("search/rc/2074")["results"]
    expected = {row["id"] for row in all_rows if key(row["vehicleNo"]) == "HR842074"}
    assert len(expected) >= 5, f"Expected the five reported HR842074 rows; found {len(expected)}"
    for spelling in ("HR-84--2074", "HR-84-2074", "hr842074", "HR 84 2074"):
        rows = get("vehicle/branches", key=spelling)["results"]
        assert {row["id"] for row in rows} == expected, f"Finance mismatch for {spelling}: {len(rows)} vs {len(expected)}"
    print(f"PASS HR842074: all {len(expected)} finance records for four spellings")

    for finance in [0] + args.finance_id:
        for suffix in ("2074", "9764", "5561"):
            full = get("search/rc/" + suffix, financeId=finance)["results"]
            lite = get("search/rc/" + suffix, lite="true", financeId=finance)["results"]
            keys = [key(row["vehicleNo"]) for row in lite]
            assert len(keys) == len(set(keys)), "Duplicate normalized RC in lite search"
            assert set(keys) == {key(row["vehicleNo"]) for row in full}, f"Missing visible RCs for {suffix}, finance {finance}"
    print("PASS full/lite RC parity, normalized deduplication and finance scopes")

    chassis = next((row["chassisNo"] for row in all_rows if len(key(row["chassisNo"])) >= 5), None)
    if chassis:
        suffix = chassis[-5:]
        full = get("search/chassis/" + urllib.parse.quote(suffix))["results"]
        lite = get("search/chassis/" + urllib.parse.quote(suffix), lite="true")["results"]
        assert {key(row["chassisNo"]) for row in lite} == {key(row["chassisNo"]) for row in full}
        expected_chassis = {row["id"] for row in full if key(row["chassisNo"]) == key(chassis)}
        rows = get("vehicle/branches", key=chassis)["results"]
        assert expected_chassis <= {row["id"] for row in rows}
        print("PASS chassis search and finance lookup parity")
    for record_id in expected:
        row = get("record/" + str(record_id))
        assert row["id"] == record_id and key(row["vehicleNo"]) == "HR842074"
    print("PASS each finance entry retrieves its own full record")
    print(f"Measured {len(timings)} HTTP requests: median={statistics.median(timings):.1f}ms, max={max(timings):.1f}ms (includes first request)")


if __name__ == "__main__":
    main()
