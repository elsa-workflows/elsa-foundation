#!/usr/bin/env python3
"""Run the PostgreSQL single-root owner-loss fixture on a fresh dedicated database."""
import asyncio
import os
from pathlib import Path
import signal
import subprocess


PROJECT = Path(__file__).resolve().parent
RUNNER = PROJECT / "bin/Release/net10.0/Kernel.Native.Run.dll"


async def launch(*args):
    return await asyncio.create_subprocess_exec(
        "dotnet", str(RUNNER), "pg-core-preview", *args,
        stdin=asyncio.subprocess.PIPE, stdout=asyncio.subprocess.PIPE,
    )


async def stop_child(child):
    if child is not None and child.returncode is None:
        try:
            child.kill()
        except ProcessLookupError:
            pass
        await child.wait()


async def run():
    owner = await launch("hold-owner")
    standby = None
    try:
        address = bookmark = epoch = None
        async for line in owner.stdout:
            name, _, value = line.decode().strip().partition(" ")
            if name == "reference":
                address = value
            elif name == "bookmark":
                bookmark = value
            else:
                print(line.decode().rstrip(), flush=True)
            if name == "owner-ready":
                epoch = int(value)
                break
        if not address or not bookmark or not epoch or owner.returncode is not None:
            raise RuntimeError("First host did not reach the persisted second wait.")

        standby = await launch("standby", address, bookmark, str(epoch))
        refused = f"standby-refused {standby.pid} {epoch}"
        async for line in standby.stdout:
            message = line.decode().strip()
            print(message, flush=True)
            if message == refused:
                break
        else:
            raise RuntimeError("Standby did not report a live-owner refusal.")

        # Kill exactly this fixture child after its live lease has refused the standby.
        owner.kill()
        if await owner.wait() != -signal.SIGKILL:
            raise RuntimeError("The waiting host did not exit through SIGKILL.")
        print(f"fixture owner {owner.pid} exited via SIGKILL", flush=True)
        standby.stdin.write(b"owner-exited\n")
        await standby.stdin.drain()
        standby.stdin.close()

        acquired = completed = False
        async for line in standby.stdout:
            message = line.decode().strip()
            print(message, flush=True)
            if message.startswith(f"standby-acquired {standby.pid} "):
                acquired = int(message.split()[2]) > epoch
            if message == "completed Ada, Grace once":
                completed = True
        if await standby.wait() != 0 or not acquired or not completed:
            raise RuntimeError("Standby did not complete the original approval workflow at a later epoch.")
    finally:
        await stop_child(standby)
        await stop_child(owner)


if __name__ == "__main__":
    if os.name != "posix" or not os.environ.get("KERNEL_PREVIEW_POSTGRES"):
        raise SystemExit("Run on a POSIX host with KERNEL_PREVIEW_POSTGRES set to a fresh dedicated database.")
    subprocess.run(["dotnet", "restore", "--locked-mode"], cwd=PROJECT, check=True)
    subprocess.run(["dotnet", "build", "-c", "Release", "--no-restore"], cwd=PROJECT, check=True)
    asyncio.run(asyncio.wait_for(run(), timeout=90))
