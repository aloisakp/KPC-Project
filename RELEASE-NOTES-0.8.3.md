# KPC Launcher 0.8.3

This update fixes Linux character export when the launcher's storage folder is on a separate partition, subvolume or drive.

- **Fixed: Linux export stopped with "Choose a regular folder: links and junctions are not supported for downloads."** This happened before KurtzPel started whenever the storage folder was below a mount point. Examples are `/home` on its own partition (as on the Steam Deck), a Btrfs subvolume (the default on Fedora) or a second drive. Inside Proton, Wine reports every mount point as a link, so the export refused a normal folder. The Linux launcher now checks the folder itself, where links and mount points can be told apart, and still refuses real links.

Linux export is still experimental. If it fails, send the export lines from the log (**Open log file**) when you report it.

Separate Windows and Linux installers, SHA-256 checksums, and build provenance are included. Install the update in your existing launcher location.
