#!/usr/bin/env bash
set -euo pipefail
runner=${1:?Pass the path to Kernel.Native.Run.dll}

dotnet "$runner" sqlite-shipped-install
dotnet "$runner" sqlite-root-ingress
for fence in checked in-batch; do
  dotnet "$runner" shipped-delay-recovery sqlite "$fence"
  dotnet "$runner" sqlite-host-lifetime "$fence" shipped
  for check in sqlite-root-timed-jobs sqlite-root-held-replies sqlite-root-terminate sqlite-root-retirement \
      sqlite-root-held-operators sqlite-root-park-opening sqlite-root-park-arrivals sqlite-root-park-inspection sqlite-root-parked-operators \
      sqlite-root-park-recovery sqlite-root-park-shipped sqlite-root-park-cohort sqlite-root-actor-reads sqlite-root-workflow-history; do
    dotnet "$runner" "$check" "$fence"
  done
done
