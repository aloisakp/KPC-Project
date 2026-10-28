# KPC Launcher 0.8.4

This update fixes downloads that hung on slow or unreliable connections, such as "Checking character export tools" waiting for over an hour.

- **Fixed: a download that stopped receiving data waited forever.** When no data arrives for 45 seconds, the launcher now drops the connection and asks again, up to eight times in a row without progress.
- **Faster on long, lossy routes.** Packages download in up to six pieces at the same time. A lost connection stalls only its own piece, which continues from where it stopped.
- **Resume.** If the tools download fails anyway, the part already received is kept, and the next attempt downloads only what is missing. The secret game instructions are never written to disk; they are received again.
- **Progress.** Package downloads show the bytes received and the current speed, so a slow download no longer looks like a hang.
- Every package is still checked against the SHA-256 in the signed release before it is used.

Separate Windows and Linux installers, SHA-256 checksums, and build provenance are included. Install the update in your existing launcher location.
