---
name: git-push
description: Ship the current branch to main. Use whenever a collaborator says "GIT PUSH" (any capitalisation). Commits everything, merges origin/main into the branch, resolves conflicts, builds and tests, merges the branch into main, pushes main, then switches back to the branch.
---

# GIT PUSH

The team's one-word ship process. Both collaborators (and their Claudes) use it. There is **no code review** step for
now: when this finishes, the work is live on `main`. Report each step briefly as you go.

Work from the repo root. Use `git` and `dotnet` from the shell (the `gh` CLI may not be installed).

## 0. Where are we?

- `git rev-parse --abbrev-ref HEAD` -> remember it as **BRANCH**.
- If a merge, rebase or cherry-pick is already in progress, stop and tell the user. Don't guess.
- If BRANCH is `main`, follow the **On main** section at the bottom instead.

## 1. Commit everything

- `git status --short`. If there are changes, stage them all (`git add -A`) and commit with a short message you write
  that describes the change (look at the diff). Show the user the message.
- Before committing, scan the staged file list. **Stop and ask** if it includes anything that looks like a secret
  (`.env`, `*token*`, `*.pem`, credentials) or a large binary/build output (`bin/`, `obj/`, `.godot/`, `export/`, `*.exe`).
- End commit messages with whatever attribution lines this session's settings require.

## 2. Bring main into the branch

```
git fetch origin
git merge origin/main
```

If it reports conflicts, resolve every conflicted file (`git diff --name-only --diff-filter=U`):

- **Pick the best result**, not just "ours" or "theirs". Read both sides and the surrounding code. When both changes
  are wanted (two new functions, two new JSON entries, two new controls), keep both. When they truly conflict, keep
  the version that matches the most recent intent and is consistent with the rest of the codebase, and note the
  choice in your report.
- JSON in `game/data/`: keep it valid, keep both sides' entries, no duplicate ids.
- `*.uid` files: keep the version from `origin/main` (Godot regenerates the rest).
- `*.csproj` / `project.godot` / `.tscn`: merge line by line; keep both sides' additions.
- No conflict markers (`<<<<<<<`, `=======`, `>>>>>>>`) may remain. Check with `git grep -n "^<<<<<<<\|^>>>>>>>"`.
- `git add` the resolved files and `git commit --no-edit` to finish the merge.

## 3. Check it builds and passes

```
dotnet build game/BeltAndBarricade.sln -nologo -v q
dotnet test sim.tests -nologo -v q
```

- If either fails **because of the merge or your conflict resolution**, fix it, commit the fix on BRANCH, and run
  both again.
- If it still fails, or the failure isn't something you can fix confidently: **stop**. Stay on BRANCH, don't touch
  `main`, and show the user the errors. "Everything clears" means: no conflicts left, build succeeds, tests pass.

## 4. Merge into main and push

```
git switch main
git pull --ff-only origin main
git merge --no-ff BRANCH -m "Merge branch 'BRANCH'"
git push origin main
```

- If `git pull --ff-only` fails, local `main` has commits that aren't on GitHub. Stop and tell the user.
- If the push is rejected because someone pushed to `main` in the meantime: undo only the merge commit you just made
  on local `main` (`git reset --hard HEAD^1`; it is safe because `main` matched `origin/main` right before the merge),
  then `git switch BRANCH` and start again from step 2. Try at most twice, then stop and tell the user.
- Do **not** push BRANCH itself to GitHub.

## 5. Back to the branch

```
git switch BRANCH
```

Finish with a short summary: what was committed, which conflicts were resolved and how, build/test results, the new
`main` commit hash, and that the user is back on BRANCH.

## On main

If the user says GIT PUSH while on `main`:

1. Commit everything (step 1).
2. `git pull --no-rebase origin main` and resolve conflicts as in step 2.
3. Build and test (step 3). If it fails and you can't fix it, stop without pushing.
4. `git push origin main`.
