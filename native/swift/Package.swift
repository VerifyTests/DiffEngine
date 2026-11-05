// swift-tools-version: 5.9
import PackageDescription

// The product name decides the file name: SwiftPM emits lib<product>.dylib, which is what
// NativeResolver probes for. Nothing else here may rename it.
let package = Package(
    name: "diffengine_viewer",
    platforms: [.macOS(.v12)],
    products: [
        .library(name: "diffengine_viewer", type: .dynamic, targets: ["Deview"]),
        // The tray's status bar item, PlatformHeads' tray ABI in a library of its own, so the
        // viewer's never carries it. build-native's mac-libraries collects it into the tray's head.
        .library(name: "diffengine_tray", type: .dynamic, targets: ["Detray"])
    ],
    dependencies: [
        // What every AppKit head does underneath its drawing, and the tray's ABI, shared with
        // BuildMonitor. A pin of PlatformHeads, moved with the other seven.
        .package(url: "https://github.com/SimonCropp/PlatformHeads", exact: "0.14.0")
    ],
    targets: [
        // Exists only to import the ABI. Swift does not guarantee struct layout, so the structs
        // have to come from the C header rather than being redeclared here.
        .target(name: "CDeview"),
        .target(
            name: "Deview",
            dependencies: [
                "CDeview",
                .product(name: "PlatformHeadsMac", package: "PlatformHeads")
            ],
            linkerSettings: [
                .linkedFramework("AppKit"),
                .linkedFramework("CoreText"),
                .linkedFramework("CoreGraphics"),
                .linkedFramework("ImageIO")
            ]),
        .target(
            name: "Detray",
            dependencies: [
                .product(name: "PlatformHeadsMacTray", package: "PlatformHeads")
            ])
    ])
