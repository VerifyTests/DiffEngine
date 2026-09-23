cmake_minimum_required(VERSION 3.24)
project(diffengine_viewer LANGUAGES C CXX)

set(CMAKE_C_STANDARD 11)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(CMAKE_C_VISIBILITY_PRESET hidden)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)
set(CMAKE_VISIBILITY_INLINES_HIDDEN ON)

if(NOT CMAKE_BUILD_TYPE AND NOT CMAKE_CONFIGURATION_TYPES)
  set(CMAKE_BUILD_TYPE Release CACHE STRING "" FORCE)
endif()

#
# Sources are fetched rather than vendored as submodules.
#
# Nothing in a normal `dotnet build` touches this directory: the built binaries are committed to
# the runtimes folder of the Linux and Mac heads, so only this file's own build needs raylib and
# imgui. Submodules would have imposed a recursive clone on every checkout to serve a path almost
# nobody takes.
#
# The versions are pinned by tag here, in the same file as the build configuration.
#
include(FetchContent)

# raylib, static so the shipped artifact is a single file per RID.
set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
set(BUILD_GAMES OFF CACHE BOOL "" FORCE)
set(CUSTOMIZE_BUILD ON CACHE BOOL "" FORCE)
# raylib 6.0 reads every SUPPORT_ flag in its config.h into an option that defaults to ON, the ones
# config.h defines as 0 included (raysan5/raylib#5844, fixed after 6.0), and CUSTOMIZE_BUILD skips
# config.h's own values. So each of those is on unless it is switched off here.
# SUPPORT_CUSTOM_FRAME_CONTROL was: EndDrawing then never put a frame on the screen, never read
# input and never waited for the next frame, so the Linux window stayed blank, took no keys and
# spun a core. A capture draws into a texture and never gets that far, which is why the pixel
# snapshots passed throughout; PixelTests.PresentWaitsForTheNextFrame is what notices. The busy
# wait loop was on the same way, and would have spun through the whole of every frame's wait once
# there was one. The rest are formats and features nothing here uses.
foreach(flag IN ITEMS
    SUPPORT_CUSTOM_FRAME_CONTROL
    SUPPORT_BUSY_WAIT_LOOP
    SUPPORT_GPU_SKINNING
    SUPPORT_FILEFORMAT_TGA
    SUPPORT_FILEFORMAT_PSD
    SUPPORT_FILEFORMAT_HDR
    SUPPORT_FILEFORMAT_PIC
    SUPPORT_FILEFORMAT_PNM
    SUPPORT_FILEFORMAT_KTX
    SUPPORT_FILEFORMAT_ASTC
    SUPPORT_FILEFORMAT_PKM
    SUPPORT_FILEFORMAT_PVR
    SUPPORT_FILEFORMAT_BDF
    SUPPORT_FILEFORMAT_FLAC)
  set(${flag} OFF CACHE BOOL "" FORCE)
endforeach()
# raylib spins for the last part of every frame by default, for timing precision nothing here
# needs: most of a millisecond of a core per frame, for as long as the viewer is open.
set(SUPPORT_PARTIALBUSY_WAIT_LOOP OFF CACHE BOOL "" FORCE)
# F12 saves a screenshot into the working directory, which is wherever the test run that started
# the viewer was launched from. Unreachable while nothing read input; reachable from here on.
set(SUPPORT_SCREEN_CAPTURE OFF CACHE BOOL "" FORCE)
set(SUPPORT_MODULE_RAUDIO OFF CACHE BOOL "" FORCE)
set(SUPPORT_MODULE_RMODELS OFF CACHE BOOL "" FORCE)
# The formats the viewer compares as pictures and raylib can decode. WebP and ICO are also
# compared, and raylib has no decoder for either, so on this platform they draw as their property
# rows alone — which is what those rows are for.
set(SUPPORT_FILEFORMAT_PNG ON CACHE BOOL "" FORCE)
set(SUPPORT_FILEFORMAT_JPG ON CACHE BOOL "" FORCE)
set(SUPPORT_FILEFORMAT_BMP ON CACHE BOOL "" FORCE)
set(SUPPORT_FILEFORMAT_GIF ON CACHE BOOL "" FORCE)

FetchContent_Declare(
  raylib
  GIT_REPOSITORY https://github.com/raysan5/raylib.git
  GIT_TAG 6.0
  GIT_SHALLOW ON)

# imgui has no CMake build of its own, so it is fetched as plain sources and compiled below.
FetchContent_Declare(
  imgui
  GIT_REPOSITORY https://github.com/ocornut/imgui.git
  GIT_TAG v1.92.9b
  GIT_SHALLOW ON)

FetchContent_MakeAvailable(raylib)
FetchContent_MakeAvailable(imgui)

add_library(diffengine_viewer SHARED
  src/deview.cpp
  ${imgui_SOURCE_DIR}/imgui.cpp
  ${imgui_SOURCE_DIR}/imgui_draw.cpp
  ${imgui_SOURCE_DIR}/imgui_tables.cpp
  ${imgui_SOURCE_DIR}/imgui_widgets.cpp)

target_include_directories(diffengine_viewer PRIVATE include ${imgui_SOURCE_DIR})
target_link_libraries(diffengine_viewer PRIVATE raylib)

# The managed side probes for a bare name; keep it identical on every platform apart from the
# platform's own prefix and extension.
set_target_properties(diffengine_viewer PROPERTIES
  OUTPUT_NAME "diffengine_viewer"
  C_VISIBILITY_PRESET hidden
  CXX_VISIBILITY_PRESET hidden)

if(MSVC)
  target_compile_options(diffengine_viewer PRIVATE /W3 /permissive-)
  target_compile_definitions(diffengine_viewer PRIVATE _CRT_SECURE_NO_WARNINGS)
else()
  target_compile_options(diffengine_viewer PRIVATE -Wall -Wextra -Wno-unused-parameter)
endif()

# Kept buildable on macOS for comparison against the Swift renderer that actually ships there,
# but nothing consumes the result: build-native builds native/swift for the osx RIDs.
if(APPLE)
  set_target_properties(diffengine_viewer PROPERTIES SUFFIX ".dylib")
endif()
