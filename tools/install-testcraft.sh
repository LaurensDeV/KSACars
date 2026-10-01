#!/usr/bin/env bash
#
# Writes the cars into your KSA vehicle library as ready-to-drive single-part craft, so testing
# them does not require a trip through the editor.
#
#     ./tools/install-testcraft.sh
#
# The Beach Buggy and the Eldorado, each its own command source: launch one from the vehicle list,
# or park it with the bridge's spawn.
#
set -euo pipefail

KSA_USER_DIR="$(find /mnt/c/Users -maxdepth 4 -type d -path '*My Games/Kitten Space Agency' 2>/dev/null | head -1 || true)"

if [[ -z "$KSA_USER_DIR" ]]; then
    echo "error: could not find the KSA user folder" >&2
    exit 1
fi

# KSA looks for "Vehicles"; Windows is case-insensitive so an existing "vehicles" is the same
# directory. Prefer whichever already exists to avoid creating a confusing duplicate.
if [[ -d "$KSA_USER_DIR/vehicles" ]]; then
    VEHICLES="$KSA_USER_DIR/vehicles"
else
    VEHICLES="$KSA_USER_DIR/Vehicles"
fi

NOW="$(date +%Y-%m-%dT%H:%M:%S.0000000)"

# Mirrors Content/Core/defaultvehicles/*/vehicle.xml: a single root part with no connections is the
# whole craft, and each car declares <Control />, so it needs no command pod.
install_car() {
  local name="$1" part="$2"
  mkdir -p "$VEHICLES/$name"
  cat > "$VEHICLES/$name/meta.toml" <<EOF
name = "$name"
created = $NOW
updated = $NOW
version = "KSACars-mod"
systems = [ "Sol", ]
EOF
  cat > "$VEHICLES/$name/vehicle.xml" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<VehicleSaveData Id="$name" ActiveSequence="0">
  <RootPartRef InstanceOf="$part" LocalInstanceId="1" Stage="0">
    <Transform>
      <Position X="0" Y="0" Z="0" />
      <Rotation X="0" Y="0" Z="0" />
      <Scale X="1" Y="1" Z="1" />
    </Transform>
  </RootPartRef>
</VehicleSaveData>
EOF
  echo "installed '$name' to $VEHICLES/$name"
}
install_car "Beach Buggy" KSACars_Prefab_Buggy
install_car "Eldorado" KSACars_Prefab_Eldorado

echo
echo "In game: launch either from the vehicle list. No editor work needed."
