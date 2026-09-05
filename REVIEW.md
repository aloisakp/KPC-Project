# Launcher source review — 2026-09-05

Scope: the download-only launcher, its tests, packaging script and release workflow.
The game, Steam client, other local launcher variants and dependency internals are
outside this source review. Version remains 0.1.0; updates target KPC-Project.

## Findings addressed

- Restored a moving, explicitly estimated download bar using Steam's compressed
  transfer total and periodic rate samples. Stops extrapolating stale rates;
  detected overlapping transfers disable estimation. Only Steam's matching
  completion message reaches 100%. Preallocated file sizes never count as progress.
- Moved existing-archive hashing off the UI thread. Cross-drive copying reports
  progress within large files and responds to cancellation without deleting the
  source. Rename retries also honor cancellation.
- Added a current-process Steam log check before changing staging or requesting
  another download. An earlier request still running after Cancel blocks a retry.
  Ambiguous failure logs require Steam to finish or restart; they do not authorize
  another writer to use staging.
- Fixed drive-root containment and added link checks for staging markers,
  completion receipts and copy destinations. Bounded receipt reads.
- Preserved UTF-8 characters split across Steam log writes.
- Removed unused rate/ETA helpers, an unused branch constant and schema property,
  unused XAML resources, an always-true update option and a redundant receipt check.
- Kept cancellation/error status visible, hid Cancel when there is no cancellable
  work, and explained an inaccessible update feed instead of showing a raw 404.
- Resolved Explorer through the Windows directory and handled folder/log opening
  failures without terminating the launcher.
- Restricted packaging cleanup to its own build output subdirectories, with
  containment and junction checks. A separate local output directory is supported.

## Security observations

No evidence of a backdoor, hidden credential collection, certificate-validation
bypass, dynamic code loading, or hidden command endpoint was found in the reviewed
launcher source. Steam authorization contacts Valve's fixed HTTPS endpoint. The
browser callback binds to IPv4 loopback and verifies the signed identity directly
with Valve. The launcher persists a public Steam ID and verification time with
per-user DPAPI, not Steam credentials or session tokens.

The other application network path is the Velopack updater for KPC-Project.
Steam owns the actual game transfer and entitlement check. The application
manifest requests normal user privileges. The release workflow is manual and
keeps private-repository releases as drafts.

## Validation

- 67 regression checks passed: browser verification, malformed and fragmented
  callbacks, account mismatches, queued/overlapping depot requests, completion
  paths, progress timing and staleness, content tampering, path containment,
  cancellable copies, and fragmented UTF-8 logs.
- Release build with current .NET analyzers and warnings treated as errors passed.
- Unused-import/private-member analyzer check passed.
- Source audit and PowerShell packaging syntax check passed.
- NuGet advisory query reported no known vulnerable direct or transitive packages.
- Steam's local console/content logs were inspected to confirm the formats used
  for sizes, speed samples, depot requests and completion messages.

This is a bounded source review, not a guarantee that no vulnerability or unused
path exists. Dependency compromise, future Steam format changes, local programs
running with the same user's privileges, and maintainer/update-feed compromise
remain trust limits. The installer is unsigned. No additional full live Steam
download was started for this review; an installed-build download is still the
end-to-end check for this revision. See SECURITY.md and BUILDING.md.
