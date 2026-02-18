# War Thunder UI overlay

### Lightweight War Thunder overlay with extra data from the API

- Flaps state
- Engine power¹
- Climb rate
- Angle
- Airbrake warning²

![](legend.png)

¹ Only for props <br>
² Only for jets

When War Thunder window is not focused, the overlay will be hidden. It will automatically come back up when the War Thunder window is focused. The overlay process can be run in the background safely, but you can stop it from the tray menu:

![](tray-icon.png)

## Download
No releases currently being published. TODO !

## Development
Uses WPF with .NET 10. Install that and debug while running War Thunder (menu is fine) to see the overlay. Also possible to see the overlay when War Thunder isn't focused by tweaking UnfocusedWindowSize.

### Publish
just `dotnet publish -c Release`

## Known issues
- The War Thunder window is detected based on the title. If any language titles the window anything other than starting with "War Thunder", the overlay will not show itself. Not sure if any language actually does this
- Plane type check currently only works correctly for the F2A ADTW, everything else is considered a prop. Detecting it correctly will need a data dump to map from.
- Flaps state is not enabled for F2A ADTW and probably many other jets too because War Thunder just doesn't pass the value. We can probably use indicators for it instead. 