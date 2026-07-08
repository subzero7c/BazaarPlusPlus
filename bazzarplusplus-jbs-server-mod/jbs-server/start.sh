#!/bin/bash
set -e

cd "$(dirname "$0")"

if [ ! -f "./jbs-server" ]; then
  echo "binary not found, building..."
  go build -o jbs-server .
fi

# 解析 -addr 参数取端口号，默认 8787
PORT=8787
for arg in "$@"; do
  if [[ "$arg" =~ ^:[0-9]+$ ]]; then
    PORT="${arg#:}"
  fi
done

# 释放端口
PID=$(lsof -ti :"$PORT" 2>/dev/null || true)
if [ -n "$PID" ]; then
  echo "Port $PORT occupied by PID $PID, killing..."
  kill -9 $PID
fi

echo "Starting JBS server on :$PORT"
./jbs-server "$@"
