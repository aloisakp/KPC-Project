# KPC Launcher 0.7.2

- Recover completed downloads left in the launcher's `.previous-...` folders,
  including the Linux Archive A download moved aside by version 0.7.1.
- Before reusing files, check their paths, sizes and every file hash against the
  requested depot version in Steam's cached manifest. Folder size alone is not
  sufficient. Steam must still be signed in to the authorized account.
- A verified saved archive is filed into the selected storage folder without
  requesting another download. The launcher then continues with the next archive.
- If the files are missing or cannot be recovered, retire only that depot's matching
  saved progress record before downloading again. Steam's stale "already complete"
  counters no longer prevent a fresh download. The old record is backed up.
- Reject an empty Steam completion before replacing an existing stored archive.
  If there are no recoverable files, the error explains how to restart Steam and retry.

**For the reported Linux issue:** keep the `.previous-...` folder where it is.
Install 0.7.2 in your existing launcher location, close the old launcher, reopen
KPC Launcher and press **Install**. Checking the saved files may take several
minutes; progress is shown in the launcher. No terminal commands or manual file
moves are needed. Reusing saved files requires Steam's readable cached manifest and
a complete, unchanged copy of the requested archive. Missing files can be downloaded again.

Automated tests reproduce the saved-backup/empty-staging situation, recover Archive A
without another Steam command, and continue to Archive B. Tests also reject altered,
incomplete or mixed files and malformed manifests. A separate test with Valve's
native SteamCMD and an anonymous SDK depot reproduced the empty completion after
moving the files, then downloaded all files successfully after retiring its progress
record; every resulting file passed its checksum. The affected player's retry is
still needed to confirm the full desktop-client flow on their installation.

Separate native Linux and Windows installers, checksums and build provenance remain available.
