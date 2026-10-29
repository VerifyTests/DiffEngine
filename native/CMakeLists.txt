cmake_minimum_required(VERSION 3.24)
project(diffengine_viewer LANGUAGES C CXX)

#
# Everything about this build that is not the viewer's own is PlatformHeads', fetched at a tag:
# the raylib and Dear ImGui it pins, the raylib 6.0 options it switches off, and how the library
# is compiled and linked. All of it was worked out here first and moved there, so that
# BuildMonitor, whose Linux head is built the same way, has the one copy too: it had its own, and
# a window that stayed blank and a library that needed libSM and libICE were each fixed here and
# then found again there.
#
# The tag is the one .github/workflows/build-native.yml calls and Directory.Packages.props names.
#
include(FetchContent)
FetchContent_Declare(
  platformheads
  GIT_REPOSITORY https://github.com/SimonCropp/PlatformHeads.git
  GIT_TAG 0.8.0
  GIT_SHALLOW ON)
FetchContent_MakeAvailable(platformheads)
include(${platformheads_SOURCE_DIR}/cmake/PlatformHeads.cmake)

platformheads_defaults()

# The formats the viewer compares as pictures and raylib can decode. WebP and ICO are also
# compared, and raylib has no decoder for either, so on this platform they draw as their property
# rows alone — which is what those rows are for.
platformheads_fetch(FORMATS PNG JPG BMP GIF)

platformheads_add_library(diffengine_viewer SOURCES src/deview.cpp)

# Threads for the threads pictures are decoded and fonts are found on, and dl for fontconfig,
# which is looked for at run time rather than linked.
find_package(Threads REQUIRED)
target_link_libraries(diffengine_viewer PRIVATE Threads::Threads ${CMAKE_DL_LIBS})

# The library stays buildable on macOS, for comparison against the Swift renderer that actually
# ships there, but nothing consumes the result: build-native builds native/swift for the osx RIDs.
