cmake_minimum_required(VERSION 3.20)
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

# raylib, static so the shipped artifact is a single file per RID.
set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
set(BUILD_GAMES OFF CACHE BOOL "" FORCE)
set(CUSTOMIZE_BUILD ON CACHE BOOL "" FORCE)
set(SUPPORT_MODULE_RAUDIO OFF CACHE BOOL "" FORCE)
set(SUPPORT_MODULE_RMODELS OFF CACHE BOOL "" FORCE)
set(SUPPORT_FILEFORMAT_PNG ON CACHE BOOL "" FORCE)
add_subdirectory(vendor/raylib EXCLUDE_FROM_ALL)

set(IMGUI_DIR ${CMAKE_CURRENT_SOURCE_DIR}/vendor/imgui)
add_library(diffengine_viewer SHARED
  src/deview.cpp
  ${IMGUI_DIR}/imgui.cpp
  ${IMGUI_DIR}/imgui_draw.cpp
  ${IMGUI_DIR}/imgui_tables.cpp
  ${IMGUI_DIR}/imgui_widgets.cpp)

target_include_directories(diffengine_viewer PRIVATE include ${IMGUI_DIR})
target_link_libraries(diffengine_viewer PRIVATE raylib)

# The managed side probes for a bare name; keep it identical on every platform apart from the
# platform's own prefix and extension.
set_target_properties(diffengine_viewer PROPERTIES
  OUTPUT_NAME "diffengine_viewer"
  C_VISIBILITY_PRESET hidden
  CXX_VISIBILITY_PRESET hidden)

if(MSVC)
  target_compile_options(diffengine_viewer PRIVATE /W3 /permissive-)
  # No iterator debugging or RTTI cost in a renderer that never throws.
  target_compile_definitions(diffengine_viewer PRIVATE _CRT_SECURE_NO_WARNINGS)
else()
  target_compile_options(diffengine_viewer PRIVATE -Wall -Wextra -Wno-unused-parameter)
endif()

if(APPLE)
  set_target_properties(diffengine_viewer PROPERTIES SUFFIX ".dylib")
endif()
