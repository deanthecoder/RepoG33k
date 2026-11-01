#!/bin/zsh
set -euo pipefail
repo_dir="$(cd "$(dirname "$0")" && pwd)"
"$repo_dir/RepoG33k/build-local.sh"
open -n "$repo_dir/RepoG33k/bin/RepoG33k.app" --args --repo "$repo_dir"
