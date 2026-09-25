# KPC Launcher 0.6.1 — Steam folder selection for Wine

- Add **Select Steam folder** when Steam cannot be found, and in **Settings → Steam**.
  Choose the installation folder containing `steam.exe`. The choice is saved and
  applies immediately without restarting the launcher.
- Add **Use automatic detection** to clear the saved folder and retry detection.
  Invalid folders are rejected without replacing the previous selection.
- Document Wine setup and add a contact address and rights-holder/takedown
  request statement to the README: **Aloisa.froyard@gmail.com**.

This addresses Steam discovery under Wine; it requires Windows Steam accessible
in the launcher's Wine environment. Native Linux Steam integration is not added,
and end-to-end Wine compatibility has not been verified for this release.
Existing Steam authorization and account checks remain in place.

The experimental relay behavior from 0.6.0 is unchanged. The Windows installer
is unsigned and may show an unknown-publisher warning. Game files are not bundled.
