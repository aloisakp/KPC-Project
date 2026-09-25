# KPC Launcher 0.7.1

- Fix Linux downloads stopping with "Steam reported an unexpected download folder"
  after Steam finishes downloading an archive. The launcher now handles Steam's
  `ubuntu12_32` / `ubuntu12_64` depot locations and backslashes in completion logs.
- Fix completed Linux downloads being treated as unfinished when retrying.
- Keep archive versions separate across the supported staging locations. Previous
  untracked downloads are retained for safety, and may be downloaded again on retry.
- Log both Steam's reported directory and the validated directory for troubleshooting.

Install the update in your existing launcher location, close the old launcher,
and open the updated launcher from the applications menu. Retry **Install**;
no terminal commands or Steam-folder changes are needed for this fix.

Regression coverage includes the reported Linux path format, two consecutive
archive downloads through a simulated Steam client, aliases, missing/ambiguous
folders, and rejection of paths outside the selected depot or containing links.
Real downloads and gameplay on the affected player's machine still need confirmation.

Windows and Linux installers remain separate. The Linux launcher is native;
the Windows game still requires Proton or Wine.
