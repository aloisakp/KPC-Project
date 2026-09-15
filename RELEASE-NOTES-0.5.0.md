# KPC Launcher 0.5.0

- **Export character** captures appearance and the supported equipped outfit from
  the normal Steam game. Enter the square to finish; the game closes and the
  launcher confirms the captured character.
- Exports use the character's name. **Open exports folder** lets you find or delete
  a file before exporting another character.
- New community accounts can import an export into unnamed rebirth, then continue
  directly into the starting story. Escape cannot bypass first creation.
- Imported clothing keeps its appearance, uses each piece's default stats, is
  labelled **[Imported]**, cannot be traded and resells for 1 GP.
- **Settings → Delete account** removes community characters, assets and progress.
  Local exports remain available for creating a new character afterward.
- Community sign-in, private downloads and gameplay connect through the public
  VPS relay. The current launcher and signed runtime no longer contain the origin
  server address. Obsolete endpoint settings and endpoint-bound sessions are
  retired during upgrade; **authorize Steam again** to reconnect.

Steam downloads and retail export still use Valve's services. Public launcher
updates use GitHub; peer matches retain their existing TURN relay transport.
Historical releases are unchanged.

The signed game update includes the accepted character introduction and the
Ensher/lobby 2 route after skipping the forest tutorial. Map 2 lighting remains
for a separate game update and does not require another launcher executable.

The installer is unsigned by Windows Authenticode. Release checksums and GitHub
build provenance accompany the assets. Tester access remains required; no game
assets, private runtime instructions or signing private keys are included in the
public installer.
