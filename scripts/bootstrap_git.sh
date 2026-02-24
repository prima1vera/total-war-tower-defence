#!/usr/bin/env bash
set -euo pipefail

# Bootstrap local repo tracking after container/session reconnects.
# Usage:
#   scripts/bootstrap_git.sh <repo_url> [base_remote_ref]
# Example:
#   scripts/bootstrap_git.sh https://github.com/org/repo.git origin/work

REPO_URL="${1:-}"
BASE_REF="${2:-origin/work}"

if [[ -z "$REPO_URL" ]]; then
  echo "Usage: $0 <repo_url> [base_remote_ref]"
  echo "Example: $0 https://github.com/org/repo.git origin/work"
  exit 1
fi

if [[ "$BASE_REF" != origin/* ]]; then
  echo "Error: base_remote_ref must be an origin/* ref (got: $BASE_REF)"
  exit 1
fi

echo "[1/7] Configure origin remote"
if git remote get-url origin >/dev/null 2>&1; then
  git remote set-url origin "$REPO_URL"
else
  git remote add origin "$REPO_URL"
fi

echo "[2/7] Fetch remote refs"
git fetch origin --prune

echo "[3/7] Ensure local main tracks origin/main"
git checkout -B main origin/main

echo "[4/7] Ensure local work tracks requested base ($BASE_REF)"
git checkout -B work "$BASE_REF"

echo "[5/7] Ensure upstream for work is origin/work"
if git show-ref --verify --quiet refs/remotes/origin/work; then
  git branch --set-upstream-to=origin/work work
else
  echo "origin/work does not exist yet; upstream will be set after first push."
fi

echo "[6/7] Display status"
git status --short --branch

echo "[7/7] Recent decorated log"
git log --oneline --decorate -5

echo "Bootstrap complete. You can now work on 'work' and open PR work -> main."
