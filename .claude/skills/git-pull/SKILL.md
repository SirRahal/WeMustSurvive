---
name: git-pull
description: Bring the latest main from GitHub into the current branch. Use whenever a collaborator says "GIT PULL" (any capitalisation). Fetches origin/main, merges it into the current branch (or pulls it if on main), resolves conflicts, keeps uncommitted work, and checks the build.
---

# GIT PULL

The team's one-word sync. It brings everyone else's work from GitHub's `main` into whatever branch you're on, so
you're building on the latest code. It never pushes anything. Report each step briefly as you go.

Work from the repo root. Use `git` and `dotnet` from the shell (the `gh` CLI may not be installed).

## 0. Where are we?

- `git rev-parse --abbrev-ref HEAD` -> remember it as **BRANCH**.
- If a merge, rebase or cherry-pick is already in progress, stop and tell the user. Don't guess.

## 1. Set uncommitted work aside

- `git status --short`. If there are uncommitted changes (including untracked files), save them with
  `git stash push --include-untracked -m "GIT PULL autostash"` and remember that you did.
- Don't commit them: GIT PULL never makes commits of the user's work. (GIT PUSH does that.)

## 2. Pull main

```
git fetch origin
```

- If BRANCH is `main`: `git merge origin/main`.
- Otherwise: `git merge origin/main` into BRANCH, then also update the local `main` to match GitHub without switching
  to it: `git fetch origin main:main` (skip this quietly if it fails; local `main` just stays behind).
- If it says "Already up to date", say so and go to step 4.

## 3. Resolve conflicts

Resolve every conflicted file (`git diff --name-only --diff-filter=U`) with the same rules as GIT PUSH:

- **Pick the best result**, not just "ours" or "theirs". Read both sides and the surrounding code. When both changes
  are wanted (two new functions, two new JSON entries, two new controls), keep both. When they truly conflict, keep
  the version that matches the most recent intent and is consistent with the rest of the codebase, and note the
  choice in your report.
- JSON in `game/data/`: keep it valid, keep both sides' entries, no duplicate ids.
- `*.uid` files: keep the version from `origin/main`.
- `*.csproj` / `project.godot` / `.tscn`: merge line by line; keep both sides' additions.
- No conflict markers may remain. Check with `git grep -n "^<<<<<<<\|^>>>>>>>"`.
- `git add` the resolved files and `git commit --no-edit` to finish the merge.

## 4. Put uncommitted work back

If you stashed in step 1: `git stash pop`.

- If that conflicts, resolve the conflicted files the same way as step 3, then `git restore --staged .` so the user's
  work is left uncommitted as it was, and `git stash drop` once everything is resolved.
- If it can't be resolved confidently, leave the stash in place (`git stash list` shows it) and tell the user.

## 5. Check the build

```
dotnet build game/BeltAndBarricade.sln -nologo -v q
dotnet test sim.tests -nologo -v q
```

- If something fails because of the merge or a conflict resolution, fix it. Leave the fix uncommitted and say so.
- If it fails for another reason (for example the user's own unfinished work), just report it. Don't undo the pull.

Finish with a short summary: how many new commits came in from `main` (and from whom, `git log --format="%h %an %s"`),
which conflicts were resolved and how, whether uncommitted work was restored, and build/test results.
