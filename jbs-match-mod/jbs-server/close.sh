#!/bin/bash
set -e

cd "$(dirname "$0")"

# Match start.sh: parse "-addr :8787" or a bare ":8787"; default to 8787.
PORT=8787
EXPECT_ADDR_VALUE=0
for arg in "$@"; do
  if [ "$EXPECT_ADDR_VALUE" = "1" ]; then
    if [[ "$arg" =~ ^:[0-9]+$ ]]; then
      PORT="${arg#:}"
    elif [[ "$arg" =~ ^[0-9]+$ ]]; then
      PORT="$arg"
    fi
    EXPECT_ADDR_VALUE=0
    continue
  fi

  case "$arg" in
    -addr|--addr)
      EXPECT_ADDR_VALUE=1
      ;;
    -addr=:*|--addr=:*)
      PORT="${arg##*:}"
      ;;
    -addr=*|--addr=*)
      PORT="${arg#*=}"
      PORT="${PORT#:}"
      ;;
    :[0-9]*)
      PORT="${arg#:}"
      ;;
    [0-9]*)
      PORT="$arg"
      ;;
  esac
done

PIDS=$(lsof -ti :"$PORT" 2>/dev/null || true)
if [ -z "$PIDS" ]; then
  echo "No JBS server process found on :$PORT"
  exit 0
fi

echo "Stopping process(es) on :$PORT: $PIDS"
kill $PIDS 2>/dev/null || true

for _ in 1 2 3 4 5; do
  sleep 1
  REMAINING=$(lsof -ti :"$PORT" 2>/dev/null || true)
  if [ -z "$REMAINING" ]; then
    echo "Stopped JBS server on :$PORT"
    exit 0
  fi
done

echo "Process(es) still running on :$PORT, forcing stop..."
kill -9 $REMAINING 2>/dev/null || true
echo "Stopped JBS server on :$PORT"
