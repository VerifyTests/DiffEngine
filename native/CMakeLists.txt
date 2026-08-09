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
set(SUPPORT_MODULE_RAUDIO OFF CACHE BOOL "" FORCE)
set(SUPPORT_MODULE_RMODELS OFF CACHE BOOL "" FORCE)
set(SUPPORT_FILEFORMAT_PNG ON CACHE BOOL "" FORCE)

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

if(APPLE)
  set_target_properties(diffengine_viewer PROPERTIES SUFFIX ".dylib")
endif()
