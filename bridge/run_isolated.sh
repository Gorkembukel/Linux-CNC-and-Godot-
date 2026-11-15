#!/bin/bash
# Usage (as root): run_isolated.sh <user> <command...>
# /usr/bin/linuxcnc kills any instance sharing its /tmp/linuxcnc.lock, and
# LinuxCNC's SysV IPC keys are fixed, so each machine gets its own /tmp, IPC
# and PID namespace; the native config copy is private too because deploy.py
# wipes it on every start. rtapi_app's control socket is not covered by any
# of those namespaces, so it gets a name unique to this run (the PID as seen
# outside the new PID namespace).
set -euo pipefail
user=$1
shift
home=$(getent passwd "$user" | cut -d: -f6)
mkdir -p "$home/godot-cnc-trainer"
export FIFO_ID=$$
exec unshare --mount --ipc --pid --fork --kill-child --mount-proc -- bash -c '
    set -e
    mount -t tmpfs tmpfs /tmp
    mount -t tmpfs -o "uid=$(id -u "$1"),gid=$(id -g "$1")" tmpfs "$2/godot-cnc-trainer"
    user=$1
    shift 2
    exec runuser -u "$user" -- env "RTAPI_FIFO_PATH=/tmp/rtapi_fifo.$FIFO_ID" "$@"
' _ "$user" "$home" "$@"
