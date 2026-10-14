#!/usr/bin/env bash
# PreToolUse hook for Edit|Write: block build output and edits to existing EF migrations.
set -u
path=$(jq -r '.tool_input.file_path // empty' 2>/dev/null)
[ -z "$path" ] && exit 0

case "$path" in
  */bin/*|*/obj/*|*/node_modules/*|*/dist/*)
    echo "Blocked: $path is build output. Do not edit it." >&2
    exit 2
    ;;
  */Business/Migrations/*)
    if [ -e "$path" ]; then
      echo "Blocked: existing migration $path must not be edited. Add a new migration instead." >&2
      exit 2
    fi
    ;;
esac
exit 0
