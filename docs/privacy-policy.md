# CAD Lens privacy policy

CAD Lens processes drawing information locally inside AutoCAD or Civil 3D. It does not send drawing data to the
publisher
or use telemetry, analytics, advertising, or automatic diagnostic uploads.

## Drawing information

The explorer reads object properties, names, and identifiers from the current drawing to build its in-memory view.
Ordinary exploration does not save a copy of the drawing or upload it.

## Local preferences

CAD Lens saves language, appearance, panel size, lens controls, filters, search text, sorting, and property choices in
JSON
files under `%LOCALAPPDATA%\CadLens`. These files stay on your computer until replaced or deleted. Close AutoCAD or
Civil 3D
and delete this directory to remove the saved preferences; CAD Lens uses defaults the next time it starts.

## Optional diagnostics

Pressing Ctrl+Shift+F12 in the CAD Lens panel explicitly creates a diagnostic JSON file on your Desktop. It can contain
the drawing filename, layer and block names, object identifiers and properties, selected objects, hatch details, host
settings, and error messages. The file is not sent automatically and remains until you delete it.

Review the file before choosing to share it for support. You can delete it directly from your Desktop.

## External links and support

Project, support, and privacy links open your default browser. GitHub handles visits and information you choose to
submit
under its [privacy statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement).
Autodesk handles App Store account and download information under its own privacy policy.

For questions, use [GitHub Issues](https://github.com/vildar82/CadLens/issues). Public support reports are visible to
other
people; omit confidential drawing information. CAD Lens does not require an account or consent to any data transmission
for local exploration.