# KPC Launcher 0.8.2

This update makes a failed character export explain why it failed, so Linux export problems can be found and fixed.

- **Export errors name the step they stopped at.** Instead of "Character export failed. See the status above", the error says which step failed, for example starting KurtzPel through Proton.
- **The log keeps the details.** The log records each export step, the reason it stopped and the export worker's exit code, plus messages from Proton, Wine or the .NET runtime that were previously dropped. On Linux it also names the Proton build used. Open it with **Open log file** on Linux or **Log** on Windows.
- If the character listener stops with an error, its error message and exit code are shown.
- If KurtzPel does not start, or its window does not open in time, the error says so instead of "The operation was canceled."

Character export works as in 0.8.1. Linux export is still experimental. If it fails, send the export lines from the log when you report it.

Separate Windows and Linux installers, SHA-256 checksums, and build provenance are included. Install the update in your existing launcher location.
