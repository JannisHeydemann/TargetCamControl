# Known Issues & Future Improvements

## Known Issues
- **HOTAS / Joystick Support:** Currently, the manual camera can only be controlled via keyboard shortcuts configured in the F1 (BepInEx) menu. Direct analog axis support via the game's official Rewired input system is currently disabled due to stability issues (NullReferenceException) during action registration.
- **Raycast Clipping:** The auto-lock raycast might occasionally hit the player's own aircraft components if `AutoLockMinDist` is set too low for very large aircraft.
- **Reflection Fragility:** The mod relies heavily on string-based reflection (e.g., `Traverse.Field("cam")`). If the game updates and renames these internal fields, the mod will break.

## Future Improvements
- **Analog Axis Integration:** Re-implement Rewired integration properly to allow mapping the TGP to joystick axes and hats.
- **Laser Rangefinder Logic:** Implement a more realistic laser rangefinder that respects line-of-sight and maximum sensor range.
- **Crosshair Customization:** Replace the simple `[X]` text with a proper HUD sprite or shader-based reticle.
- **Target Lead Prediction:** Add a basic lead indicator on the MFD when a moving target is manually tracked.
- **Publicized DLLs:** Switch the build process to use `Krafs.Publicizer` for cleaner code (avoiding `Traverse` where possible).
