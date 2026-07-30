# Bundled LibVLC runtimes

Release artifacts use the following runtime layout:

```text
libvlc/
├── linux-x64/
│   ├── libvlc.so.5
│   ├── libvlccore.so.9
│   ├── deps/
│   └── plugins/
├── win-x64/
│   ├── libvlc.dll
│   ├── libvlccore.dll
│   └── plugins/
└── win-arm64/
```

Windows files are supplied by `VideoLAN.LibVLC.Windows`. macOS x64 uses
`VideoLAN.LibVLC.Mac`. Linux files are assembled into the publish directory by
`scripts/bundle-linux-libvlc.sh`; native binaries are intentionally not committed
to Git

LibVLCSharp 3 does not accept `Core.Initialize(path)` on Linux. Rezui therefore
registers a .NET native-library resolver for its bundled, versioned
`libvlc.so.5`, while the generated launcher supplies `LD_LIBRARY_PATH` and
`VLC_PLUGIN_PATH` before the .NET process starts
