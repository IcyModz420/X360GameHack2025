#!/bin/bash
# find_jrunner_comport.sh
# Usage: ./find_jrunner_comport.sh <VID> <PID> [output_file]
# Example: ./find_jrunner_comport.sh 1a86 55d3
# VID and PID should be hex values (with or without 0x prefix)
#
# Output file defaults to /tmp/jrunner_comport.txt
# One COM port per line, ordered by USB interface number (interface 0 first).
# If a device exposes multiple interfaces with tty nodes, all are included.

set -euo pipefail

# --- Argument handling ---
if [[ $# -lt 2 ]]; then
    echo "Usage: $0 <VID> <PID> [output_file]"
    echo "  VID/PID: USB vendor/product ID in hex (e.g. 1a86 or 0x1a86)"
    exit 1
fi

# Strip optional 0x prefix and lowercase
VID=$(echo "$1" | sed 's/^0x//I' | tr '[:upper:]' '[:lower:]')
PID=$(echo "$2" | sed 's/^0x//I' | tr '[:upper:]' '[:lower:]')
OUTPUT_FILE="${3:-/tmp/jrunner_comport.txt}"

DOSDEVICES="${WINEPREFIX:-$HOME/.wine}/dosdevices"

# ---------------------------------------------------------------------------
# find_tty_devices_by_interface VID PID
#
# Walks /sys/bus/usb/devices/ to find every USB device matching VID:PID.
# For each matching device, enumerates its interface sub-directories
# (e.g. 1-2:1.0, 1-2:1.1 …).  The trailing digit after the dot is the
# bInterfaceNumber.  Within each interface directory we look for a tty
# sub-device and emit:
#
#   <interface_number> <tty_name>
#
# Lines are emitted in interface-number order so the caller can rely on
# stable ordering even when sysfs readdir order varies.
# ---------------------------------------------------------------------------
find_tty_devices_by_interface() {
    local vid="$1"
    local pid="$2"

    # Collect results as "ifnum ttyXXX" pairs, then sort numerically on ifnum
    local results=()

    for dev_path in /sys/bus/usb/devices/*/; do
        local vendor_file="${dev_path}idVendor"
        local product_file="${dev_path}idProduct"

        [[ -f "$vendor_file" && -f "$product_file" ]] || continue

        local dev_vid dev_pid
        dev_vid=$(tr '[:upper:]' '[:lower:]' < "$vendor_file")
        dev_pid=$(tr '[:upper:]' '[:lower:]' < "$product_file")

        [[ "$dev_vid" == "$vid" && "$dev_pid" == "$pid" ]] || continue

        # dev_path is the root USB device dir (e.g. /sys/bus/usb/devices/1-2/)
        # Interface dirs live directly beneath it, named <bus>-<port>:<cfg>.<iface>
        # We enumerate them explicitly so we can capture the interface number.
        local dev_name
        dev_name=$(basename "$dev_path")

        for iface_path in "${dev_path}"${dev_name}:*/; do
            [[ -d "$iface_path" ]] || continue

            # Extract bInterfaceNumber from the sysfs attribute if present,
            # otherwise fall back to parsing the directory name suffix (.<N>).
            local ifnum
            if [[ -f "${iface_path}bInterfaceNumber" ]]; then
                ifnum=$(cat "${iface_path}bInterfaceNumber")
                # Remove leading zeros so sort -n works correctly
                ifnum=$((10#$ifnum))
            else
                # Directory name pattern: 1-2:1.<N>
                ifnum=$(basename "$iface_path" | grep -oP '\.\K[0-9]+$' || echo "999")
                ifnum=$((10#$ifnum))
            fi

            # Look for a tty node anywhere under this interface directory
            local tty
            tty=$(find "$iface_path" -name "tty*" -maxdepth 6 2>/dev/null \
                  | grep -oP '(?<=/)(tty[^/]+)' | grep -v '^tty$' | head -n1 || true)

            [[ -n "$tty" ]] || continue

            results+=("${ifnum} ${tty}")
        done
    done

    # Sort by interface number (numeric), then emit
    printf '%s\n' "${results[@]}" | sort -n
}

# ---------------------------------------------------------------------------
# resolve_jrunner_comport TTY_NAME
#
# Given a bare tty name (e.g. ttyUSB0), return the matching Wine COM port
# (e.g. COM3).  Returns empty string if not found.
# ---------------------------------------------------------------------------
resolve_jrunner_comport() {
    local tty="$1"
    local com_port=""

    # Strategy 1: dosdevices symlinks — fastest, no wine binary needed
    if [[ -d "$DOSDEVICES" ]]; then
        for link in "$DOSDEVICES"/com*; do
            [[ -L "$link" ]] || continue
            local target
            target=$(readlink -f "$link" 2>/dev/null || true)
            if [[ "$target" == "/dev/$tty" ]]; then
                com_port=$(basename "$link" | tr '[:lower:]' '[:upper:]')
                break
            fi
        done
    fi

    echo "$com_port"
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

# Build an ordered list of (ifnum, tty) pairs
mapfile -t IFACE_TTY_PAIRS < <(find_tty_devices_by_interface "$VID" "$PID")

if [[ ${#IFACE_TTY_PAIRS[@]} -eq 0 ]]; then
    echo "Error: No tty devices found for VID=$VID PID=$PID" >&2
    exit 2
fi

COM_PORTS=()
MISSING=()

for pair in "${IFACE_TTY_PAIRS[@]}"; do
    ifnum=$(echo "$pair" | awk '{print $1}')
    tty=$(echo "$pair"   | awk '{print $2}')

    com=$(resolve_jrunner_comport "$tty")

    if [[ -n "$com" ]]; then
        COM_PORTS+=("$com")
        echo "Interface ${ifnum}: /dev/${tty} -> ${com}"
    else
        MISSING+=("/dev/${tty} (interface ${ifnum})")
        echo "Warning: /dev/${tty} (interface ${ifnum}) has no Wine COM port mapping" >&2
    fi
done

if [[ ${#COM_PORTS[@]} -eq 0 ]]; then
    echo "Error: Devices found but none are mapped to a Wine COM port." >&2
    echo "Make sure devices are mapped in your Wine prefix dosdevices or registry." >&2
    exit 3
fi

if [[ ${#MISSING[@]} -gt 0 ]]; then
    echo "Warning: ${#MISSING[@]} interface(s) had no COM port mapping and were skipped." >&2
fi

# Write one COM port per line, ordered by interface number, no trailing newline
printf '%s\n' "${COM_PORTS[@]}" | head -c -1 > "$OUTPUT_FILE"

echo "Written ${#COM_PORTS[@]} COM port(s) to: ${OUTPUT_FILE}"
