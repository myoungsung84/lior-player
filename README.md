# lior-player

`libmpv` metadata is kept under `Lior/vendor/mpv` and upgraded manually.

## Local run prerequisites

- `Lior/vendor/mpv/VERSION.txt` stays in the repository as the tracked version marker.
- `Lior/vendor/mpv/mpv-2.dll` is not committed to the repository.
- Before running the app locally, place a compatible `mpv-2.dll` at `Lior/vendor/mpv/mpv-2.dll`.
- If the DLL is missing, the app may fail to start or the player service may not initialize correctly.

Do not add automatic runtime download/update logic for `mpv-2.dll`.
