#!/usr/bin/env bash
# PreToolUse hook for Edit|Write: block build output and edits to EF migrations that
# are already on main. A migration created on a task branch stays editable until merged.
set -u
path=$(jq -r '.tool_input.file_path // empty' 2>/dev/null)
[ -z "$path" ] && exit 0

case "$path" in
  */bin/*|*/obj/*|*/node_modules/*|*/dist/*)
    echo "Blocked: $path is build output. Do not edit it." >&2
    exit 2
    ;;
  */Business/Migrations/*)
    dir=$(dirname "$path")
    if [ -d "$dir" ] && root=$(git -C "$dir" rev-parse --show-toplevel 2>/dev/null); then
      rel=${path#"$root"/}
      if git -C "$root" cat-file -e "main:$rel" 2>/dev/null; then
        echo "Blocked: $rel is a migration already on main. Add a new migration instead." >&2
        exit 2
      fi
    fi
    ;;
esac
exit 0
