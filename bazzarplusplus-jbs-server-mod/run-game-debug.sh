#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$SCRIPT_DIR/src/BazaarPlusPlus.JbsServer/BazaarPlusPlus.JbsServer.csproj"

CONFIG="${CONFIG:-Debug}"
BUILD="${BUILD:-1}"
LAUNCH="${LAUNCH:-1}"
DOTNET="${DOTNET:-}"

if [[ -z "$DOTNET" ]]; then
    if command -v dotnet >/dev/null 2>&1; then
        DOTNET="dotnet"
    elif [[ -x "$HOME/.dotnet/dotnet" ]]; then
        DOTNET="$HOME/.dotnet/dotnet"
    else
        echo "dotnet was not found. Set DOTNET=/path/to/dotnet and run again." >&2
        exit 1
    fi
fi

case "$(uname -s)" in
    Darwin)
        DEFAULT_GAME_ROOT="$HOME/Library/Application Support/Steam/steamapps/common/The Bazaar"
        ;;
    MINGW*|MSYS*|CYGWIN*)
        DEFAULT_GAME_ROOT="/c/Program Files (x86)/Steam/steamapps/common/The Bazaar"
        ;;
    *)
        echo "Unsupported platform: $(uname -s)" >&2
        exit 1
        ;;
esac

GAME_ROOT="${GAME_ROOT:-$DEFAULT_GAME_ROOT}"

if [[ ! -d "$GAME_ROOT" ]]; then
    echo "The Bazaar install was not found: $GAME_ROOT" >&2
    echo "Set GAME_ROOT to your test install path and run again." >&2
    exit 1
fi

if [[ "$BUILD" == "1" ]]; then
    "$DOTNET" build "$PROJECT" -c "$CONFIG"
fi

if [[ "$LAUNCH" != "1" ]]; then
    exit 0
fi

case "$(uname -s)" in
    Darwin)
        cd "$GAME_ROOT"
        if [[ "$(cat "$GAME_ROOT/.bpp-launch-mode" 2>/dev/null || true)" == "trampoline" ]]; then
            EXE_NAME="$(plutil -extract CFBundleExecutable raw -o - "$GAME_ROOT/TheBazaar.app/Contents/Info.plist")"
            EXE="$GAME_ROOT/TheBazaar.app/Contents/MacOS/$EXE_NAME"
            if [[ ! -x "$EXE" ]]; then
                echo "The Bazaar executable is missing or not executable: $EXE" >&2
                exit 1
            fi

            exec "$EXE" "$@"
        fi

        LAUNCHER="$GAME_ROOT/run_bepinex.sh"
        if [[ -x "$LAUNCHER" ]]; then
            exec "$LAUNCHER" "$GAME_ROOT/TheBazaar.app" "$@"
        fi

        if [[ -f "$LAUNCHER" ]]; then
            exec sh "$LAUNCHER" "$GAME_ROOT/TheBazaar.app" "$@"
        fi

        echo "BepInEx launcher is missing: $LAUNCHER" >&2
        exit 1
        ;;
    MINGW*|MSYS*|CYGWIN*)
        EXE="$GAME_ROOT/TheBazaar.exe"
        if [[ ! -x "$EXE" ]]; then
            echo "The Bazaar executable is missing or not executable: $EXE" >&2
            exit 1
        fi

        cd "$GAME_ROOT"
        exec "$EXE" "$@"
        ;;
esac
