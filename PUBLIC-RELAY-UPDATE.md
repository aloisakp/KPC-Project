# Normal updater and selected installation

Baseline SHA: 880d4f8; previous public version 0.5.3 / 8495cbf.
Root cause: the development shell disabled Velopack and forced sandbox state.
Authoritative owner: public launcher configuration and installed updater.
Replaced behavior: normal starts reuse %LOCALAPPDATA%/KPCLauncher settings and
the selected StorageRoot; only explicit development test overrides remain isolated.
The fixed pinned tester URL moves to VPS11109. A valid session from the same
operator's old pinned public endpoint can migrate; arbitrary origins cannot.
The deployed service must import existing session/grant data before public cutover.

Files intentionally touched: Program, LauncherUpdater, LauncherConfig, TesterClient
and their security tests. No game files, game keys or private runtime are embedded.
Focused/broad checks: all 162 launcher security checks pass, including endpoint
binding, session migration, archive handling and non-installed update refusal.
Build/publish checks: complete installer publication remains pending.
Live observation: unverified; no installed launcher has changed in this source slice.
Known gaps: release UI/version and signed runtime deployment are separate changes.
Rollback: git revert this record's commit before publication; after publication
ship a higher recovery version with the old route, never rewrite a published tag.
