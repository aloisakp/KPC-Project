# KPC Launcher 0.8.0

This update changes how the tester game is prepared. **No game key is used, and none of the game's checks is switched off.**

- **No key.** The launcher no longer reads the game's encryption key from your game executable. It never asks for, stores or sends one.
- **Stacking instead of rebuilding.** **Merge** now prepares `KurtzPel-Tester` from your two downloads as they are: the 2026 version with its unmodified game executable, plus every signed game package of the July 2021 version, unchanged and under its original name, in `TheChase/Content/Paks/2021`, and the 2021 movies and other loose files that the 2026 version lacks. The game verifies each package's signature itself; where both versions have a file, the 2026 file is used.
- **Nothing built or patched.** The launcher no longer builds replacement game packages, changes bytes of the game executable, or turns off the game's package signature check.
- **Less disk space.** The prepared game links to the downloaded files instead of copying them, so it needs very little extra space. A storage drive that cannot link files (for example FAT or exFAT) falls back to copying.
- **Merge once after updating.** Game folders prepared by 0.7 or older are not playable with this version. Press **Merge**; the old folder is replaced after the new one is verified. Your downloads are not changed.
- Private updates that still ask for the old key preparation are refused, with a message to wait for the matching server update.
- Fixed garbled characters in the tester status ("Tester access active · game ready") and in the README.
- Removed old development scripts that pointed at folders on the maintainer's computer.

Tester **Merge** and **Play** need the matching server update (merge version `merge-4-stacking`). Automated tests build the stacked layout from test folders and check the verification rules; a full Merge and Play with the real downloads is still to be confirmed.

Separate Windows and Linux installers, SHA-256 checksums, and build provenance are included. Install the update in your existing launcher location.
