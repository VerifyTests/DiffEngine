// swift-tools-version: 5.9
import PackageDescription

// The product name decides the file name: SwiftPM emits lib<product>.dylib, which is what
// NativeResolver probes for. Nothing else here may rename it.
let package = Package(
    name: "diffengine_viewer",
    platforms: [.macOS(.v12)],
    products: [
        .library(name: "diffengine_viewer", type: .dynamic, targets: ["Deview"])
    ],
    targets: [
        // Exists only to import the ABI. Swift does not guarantee struct layout, so the structs
        // have to come from the C header rather than being redeclared here.
        .target(name: "CDeview"),
        .target(
            name: "Deview",
            dependencies: ["CDeview"],
            linkerSettings: [
                .linkedFramework("AppKit"),
                .linkedFramework("CoreText"),
                .linkedFramework("CoreGraphics"),
                .linkedFramework("ImageIO")
            ])
    ])
