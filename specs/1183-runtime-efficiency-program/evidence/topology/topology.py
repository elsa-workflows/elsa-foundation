"""Validate a declared future 2/4-worker inventory; perform no resource operation."""
import ipaddress
import json
from pathlib import PurePosixPath
import re
import sys


def require(condition, message):
    if not condition:
        raise ValueError(message)


def validate(data):
    require(isinstance(data, dict), "Topology must be an object")
    require(type(data.get("schemaVersion")) is int and data["schemaVersion"] == 2,
            "Require topology schema 2 with explicit driver placement")
    run = data.get("runId")
    require(isinstance(run, str) and re.fullmatch(r"efficiency-[a-z0-9]{8,32}", run),
            "Require a fresh task-specific run identifier")
    count = data.get("nodeCount")
    workers = data.get("workers")
    require(type(count) is int and count in (2, 4) and isinstance(workers, list)
            and len(workers) == count, "Require exactly two or four explicit workers")
    placement = data.get("driverPlacement")
    require(placement in ("DatabaseHost", "SeparateHost"),
            "Declare DatabaseHost or SeparateHost driver placement")
    hosts = [("database", data.get("database"))]
    if placement == "SeparateHost":
        hosts.append(("driver", data.get("driver")))
    else:
        require("driver" not in data,
                "A database-cohosted driver must not declare another driver host")
    hosts.extend(("worker", worker) for worker in workers)
    identities, resources, addresses = [], [], []
    rfc1918 = tuple(ipaddress.ip_network(n) for n in
                    ("10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"))
    for role, host in hosts:
        require(isinstance(host, dict), "Every host must be an object")
        identity = host.get("machineId")
        resource = host.get("resourceId")
        fingerprint = host.get("sshHostKeySha256")
        require(isinstance(identity, str) and re.fullmatch(r"[a-fA-F0-9]{32}", identity),
                "Require a declared Linux machine-id")
        require(isinstance(resource, str) and re.fullmatch(
            r"/subscriptions/[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-"
            r"[a-fA-F0-9]{4}-[a-fA-F0-9]{12}/resourceGroups/[^/]+/providers/"
            r"Microsoft\.Compute/virtualMachines/[^/]+", resource, re.IGNORECASE),
            "Require a declared Azure VM resource identifier")
        require(isinstance(fingerprint, str) and
                re.fullmatch(r"SHA256:[A-Za-z0-9+/]{43}", fingerprint),
                "Require a declared SSH host-key fingerprint")
        try:
            address = ipaddress.ip_address(host.get("privateAddress", ""))
        except (ValueError, TypeError) as error:
            raise ValueError("Require an explicit private IPv4 address") from error
        require(isinstance(address, ipaddress.IPv4Address) and
                any(address in network for network in rfc1918),
                "Require an RFC1918 IPv4 address on the fixture network")
        user = host.get("sshUser")
        require(isinstance(user, str) and re.fullmatch(r"[a-z_][a-z0-9_-]{0,31}", user),
                "Require an explicit finite SSH user")
        root = host.get("taskRoot")
        require(isinstance(root, str), "Require a dedicated task root")
        path = PurePosixPath(root)
        require(path.is_absolute() and path.name == run and ".." not in path.parts
                and len(path.parts) >= 3 and str(path) == root,
                "Require a normalized absolute root ending in this fresh run ID")
        if role == "worker":
            require(type(host.get("port")) is int and 1024 <= host["port"] <= 65535,
                    "Require a worker port; free-port and listener ownership remain unproved")
        identities.append(identity.lower())
        resources.append(resource.lower())
        addresses.append(str(address))
    require(all(len(set(values)) == len(values) for values in
                (identities, resources, addresses)),
            "Declared workers and distinct roles must use distinct hosts")
    require(all(isinstance(data.get(key), str) and
                re.fullmatch(r"[a-f0-9]{40}", data[key])
                for key in ("sourceCommit", "sourceTree")),
            "Pin the actual source commit and tree")
    hashes = data.get("binarySha256")
    require(isinstance(hashes, dict) and
            {"Elsa.Workbench.dll", "Elsa.Workflows.Runtime.dll"}.issubset(hashes) and
            all(isinstance(value, str) and re.fullmatch(r"[a-f0-9]{64}", value)
                for value in hashes.values()),
            "Declare the common rebuilt binary cohort hashes")
    return {
        "manifestValid": True,
        "runId": run,
        "workers": count,
        "driverPlacement": placement,
        "declaredIndependentHosts": len(hosts),
        "boundary": (
            "Manifest validation only. Supplied identities, keys, binaries and topology "
            "need live corroboration; actor participation, capacity, timing, ownership, "
            "cleanup, approval and engine qualification are not established."
        ),
    }


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Usage: topology.py <inventory.json>; no network or resource action")
    with open(sys.argv[1], encoding="utf-8") as stream:
        result = validate(json.load(stream))
    print(json.dumps(result))
