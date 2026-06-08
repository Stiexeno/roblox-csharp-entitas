---
name: commit
description: Inspect unstaged + untracked changes, group them by logical concern, and commit each group as a separate commit on the current branch. Use when changes span multiple unrelated topics and want a clean history.
---

Split the user's pending changes into logical commits.

## Steps

### 1. Survey the working tree

Run these in parallel:

- `git status` — full picture of modified, deleted, untracked files
- `git diff --stat` — change-size context per file
- `git log --oneline -10` — match the repo's commit-message style
- `git ls-files --others --exclude-standard <untracked-folders>` — expand any untracked directories so you see every new file

### 2. Inspect the actual diffs

Read the full diffs for code/config files. **Never** group based on filenames alone — open the diff and confirm what each change does. For untracked files, read them directly.

### 3. Group by logical concern

Each commit should answer one question: *"what did this change accomplish?"*

**Bias toward more, smaller commits.** A single new feature almost always decomposes into multiple layer- or concern-specific commits. Do NOT lump an entire feature into one commit just because the parts share a theme — if a reviewer would want to read the backend changes independently from the UI changes, they belong in separate commits.

**But never split files that depend on each other and can't work apart.** If commit A would leave the codebase broken without commit B (compile errors, missing types, dangling references both ways), they belong together. Split only along directions where the *earlier* commit stands on its own:

- ECS / domain backend → server-only systems → client-only systems → UI. **Order matters — commit the dependency first.**
- If two files reference each other (mutual dependency), keep them in one commit.
- If a client-side change requires a new replicated component to even compile, do not commit the client alone — either bundle them, or commit components → server → client in that order.

The test: after each commit, the codebase must compile. If a proposed split would leave a broken intermediate state, either reorder so the dependency lands first, or merge the dependent pieces into a single commit.

Within a single feature, split by sub-concern. Typical layers (commit each as its own group when the changes exist):

- **ECS / domain backend** — components, systems, services, factories, feature class, DI binding.
- **Server-only systems** — files ending in `.server.cs` (damage application, spawning).
- **Client-only systems** — files ending in `.client.cs` (VFX reactors, HUD updaters).
- **Configs & data** — new config classes, DataStore wrappers.
- **UI** — UI screens / widgets when the project has a UI layer.
- **Bootstrap wiring** — entry-point + installer changes.

Other splitting rules:

- **Source code + its codegen output** — keep generated files (`src/Generated/`) OUT of commits unless the user explicitly asks (`.gitignore` should cover this in most projects; if not, the trailing commit is `Regenerated entities` and lives alone).
- **Refactors / file moves** in their own commit, separate from new behavior.
- **Component additions** separate from the systems that use them, when the component is genuinely reusable.
- **Unrelated incidental fixes** (typos, unused imports) in a small dedicated commit.
- **A reviewer must be able to read each commit independently** — that's the test.
- **Order commits by dependency.** When splitting layers, the depended-on layer ships first.

### 4. Present the plan, then execute

Before committing, write out the proposed commits as a numbered list with the title of each and a one-line rationale. The list order **is** the commit order, so place dependencies before dependents. Then proceed to execute them in order — no need to wait for confirmation unless the grouping is non-obvious or risky.

### 5. Stage and commit one group at a time

For each group:

- `git add` **specific paths only** — never `git add -A` or `git add .` (those can sweep in secrets, build artifacts, or unrelated edits).
- Commit with a HEREDOC message.
- **Message style:** short single-line explanation starting with a capital letter. Do NOT use conventional-commit prefixes like `feat:`, `fix:`, `refactor:`, `chore:` etc. Just describe what the change does (e.g. `Add Combat backend`, `Wire HitFlash VFX on client`, `Bump replication batch size to 400`).
- Always include the `Co-Authored-By` trailer if working with Claude.
- If a pre-commit hook fails, fix the underlying issue and create a NEW commit (do NOT `--amend`).

### 6. Verify

After all commits land:

- `git status` — must be clean (or only contain files the user explicitly asked to leave unstaged).
- `git log --oneline -<N+2>` — show the new commits at the top.

## Rules

- **Never** push to remote (unless explicitly asked).
- **Never** amend existing commits.
- **Never** use `--no-verify` or skip hooks.
- **Never** delete untracked files or branches as part of "cleanup."
- If you find files that look sensitive (`.env`, credentials, keys), warn the user and skip them — do not commit.
- Use plain short titles starting with a capital letter — no `feat:` / `fix:` / `refactor:` / `chore:` prefixes.
- Codegen / generated files: bundle into one trailing `Regenerated entities` commit unless they are the *only* change in scope.
- Keep each commit independently reviewable — a reader should understand the "why" from the title alone.
