#!/bin/bash
# Runs every Assets/Editor/*Check.cs in Unity batch mode and writes PASS/FAIL per check.
# Usage: Tools/run-checks.sh [output-dir]   (default: a new temp dir; summary printed at the end)
set -u
PROJECT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity"
OUT="${1:-$(mktemp -d)}"
SUMMARY="$OUT/checks-summary.txt"
mkdir -p "$OUT/logs" "$OUT/save" "$OUT/sweep"
: > "$SUMMARY"
passed=0
failed=0
for file in "$PROJECT"/Assets/Editor/*Check.cs; do
  name="$(basename "$file" .cs)"
  log="$OUT/logs/$name.log"
  extra=()
  # PlayerAccessCheck needs a scratch save folder without save-v1.json; SimulationCheck writes its save there and
  # refuses to run without it. MapSweepCheck writes into $SWEEP_DIR.
  if [ "$name" = "PlayerAccessCheck" ] || [ "$name" = "SimulationCheck" ]; then mkdir -p "$OUT/save/$name"; extra=(--save-directory "$OUT/save/$name"); fi
  SWEEP_DIR="$OUT/sweep" "$UNITY" -batchmode -nographics -projectPath "$PROJECT" \
    -executeMethod "PeninsulaTime.$name.Run" -logFile "$log" ${extra[@]+"${extra[@]}"}
  code=$?
  if [ "$code" -eq 0 ]; then
    passed=$((passed + 1)); echo "PASS $name" >> "$SUMMARY"
  else
    failed=$((failed + 1)); echo "FAIL $name (exit $code) log $log" >> "$SUMMARY"
  fi
done
echo "passed $passed, failed $failed" >> "$SUMMARY"
cat "$SUMMARY"
[ "$failed" -eq 0 ]
