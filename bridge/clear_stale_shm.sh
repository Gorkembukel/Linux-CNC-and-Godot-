#!/bin/bash
# Removes ALL SysV IPC (shared memory, semaphores, message queues) owned by the
# current user. LinuxCNC's uspace NML/HAL/RTAPI layer uses fixed SysV IPC keys;
# if a previous instance didn't shut down cleanly (killed instead of given a
# chance to release things), stale segments *or semaphores left in a locked
# state* at those keys either block the next instance ("emcStatusBuffer
# invalid") or make it hang for a long, unpredictable time waiting on a mutex
# nothing will ever release. This is safe on a dedicated LinuxCNC dev/training
# box where nothing else uses SysV IPC; it is run before every start.
set -uo pipefail
WHOAMI=$(whoami)

ipcs -m | awk -v me="$WHOAMI" 'NR>3 && NF>=3 && $3==me {print $2}' | while read -r id; do
    echo "removing stale shm $id"
    ipcrm -m "$id" 2>/dev/null || true
done

ipcs -s | awk -v me="$WHOAMI" 'NR>3 && NF>=3 && $3==me {print $2}' | while read -r id; do
    echo "removing stale sem $id"
    ipcrm -s "$id" 2>/dev/null || true
done

ipcs -q | awk -v me="$WHOAMI" 'NR>3 && NF>=3 && $3==me {print $2}' | while read -r id; do
    echo "removing stale msg queue $id"
    ipcrm -q "$id" 2>/dev/null || true
done
