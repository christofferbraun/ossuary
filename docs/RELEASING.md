# Releasing

How a version of Ossuary reaches the Steam Workshop.

## Why this is not one button

Two steps cannot run on a hosted runner, and pretending otherwise would produce
a pipeline that fails at the worst moment.

**The mod cannot be built in CI.** `Ossuary.dll` references `sts2.dll`,
`0Harmony.dll` and `GodotSharp.dll` from a Slay the Spire 2 install. Those are
proprietary and not redistributable, so they are neither committed nor
obtainable by a runner. This is the same reason CI tests only
`Ossuary.Grading` — see `COMPAT.md`.

**The Workshop upload needs a Steam client.** Mega Crit's uploader
(`github.com/megacrit/sts2-mod-uploader`) calls `SteamAPI.InitEx()`, which talks
to a running Steam client signed into an account that owns the game. A
GitHub-hosted runner has no Steam client and no session. There is no token that
substitutes for this.

So the split is:

| Step | Where | What |
| --- | --- | --- |
| Verify | CI (`release.yml`, on a `v*` tag) | tests, version/tag agreement, manifest still says it changes nothing, Workshop metadata is publishable, ratings table is intact, draft GitHub release |
| Build & package | your machine | `tools\package.ps1` |
| Publish | your machine, Steam running | `tools\publish.ps1` |

If you ever want the publish automated, the answer is a **self-hosted runner on
a machine with Steam signed in** — not a secret in GitHub Actions.

## Releasing

1. **Refresh the ratings if you want to**, or let the weekly workflow do it.
   `refresh-data.yml` opens a pull request when Codex publishes a new snapshot.

2. **Set the version** in `Directory.Build.props`. It lives in exactly one place
   and everything else reads it.

3. **Tag it.** CI verifies the tag matches the declared version and drafts the
   release. A mismatch fails the tag rather than producing a bad build.

   ```powershell
   git tag v0.2.0
   git push origin v0.2.0
   ```

4. **Package.** Builds the mod, assembles the Workshop workspace, and checks
   what is about to be published.

   ```powershell
   .\tools\package.ps1
   # or give subscribers a better note than the last commit subject:
   .\tools\package.ps1 -ChangeNote "Adds the incoming-damage forecast."
   ```

5. **Publish.** Start Steam and sign in first. The script prints what it is
   about to do and asks before uploading.

   ```powershell
   .\tools\publish.ps1
   ```

6. **Commit `workshop/mod_id.txt`** if this was the first upload. Without it the
   next release creates a *second* Workshop item instead of updating the one
   people are subscribed to. `package.ps1` warns when it is missing.

7. **Make it public** when you are happy with how the listing looks.
   `workshop.json` ships `"visibility": "private"` on purpose, so a release can
   never accidentally go public before anyone has looked at it. Change it there,
   or flip it on the Workshop page.

## The workspace

`package.ps1` produces `build\workshop\`, which is what the uploader consumes:

```
content\         Ossuary.dll and Ossuary.json — what subscribers receive
workshop.json    title, description, visibility, change note
image.png        the Workshop thumbnail, under Steam's 1 MB limit
mod_id.txt       the Workshop item id, once there is one
```

The committed sources are in `workshop\`. Only the change note differs per
release, so it is substituted at package time rather than kept in the committed
file where it would go stale.

To regenerate the thumbnail after a UI change:

```powershell
python tools\make-preview.py
```

## Before the first public release

- Read `COMPLIANCE.md` once more. It is dated, and the store page's missing
  Workshop tag is worth re-checking.
- The Workshop description should carry the same two disclosures the README
  does: that Ossuary reads and displays only, and that runs played with any mod
  loaded are flagged as modded by the game. Both are in `workshop.json` already.
- Attribution to Spire Codex belongs in the Workshop description, since most
  people will never read the README. It is in there — keep it there.
