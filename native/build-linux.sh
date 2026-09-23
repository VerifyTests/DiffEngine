#!/usr/bin/env bash
# Builds build/libdiffengine_viewer.so inside a manylinux_2_28 container, whose glibc 2.28 is the
# floor the library then needs - see the Linux build step in .github/workflows/build-native.yml.
# Runs from the repository root:
#
#   docker run --rm -v "$PWD:/src" -w /src "quay.io/pypa/manylinux_2_28_$(uname -m)" bash native/build-linux.sh
set -euo pipefail

# What raylib's bundled GLFW builds against, for both its X11 and Wayland backends: the set the
# runner used to install, under this distribution's names.
dnf install -y \
  git pkgconfig \
  libX11-devel libXext-devel libXrandr-devel libXi-devel libXcursor-devel libXinerama-devel \
  mesa-libGL-devel wayland-devel libxkbcommon-devel

# native/CMakeLists.txt needs CMake 3.24, newer than the distribution's own. The image carries
# current releases through pipx, so these are only installed where it does not.
export PATH="$HOME/.local/bin:$PATH"
for tool in cmake ninja; do
  if ! command -v "$tool" > /dev/null 2>&1; then
    pipx install "$tool"
  fi
done

cmake -S native -B build -G Ninja -DCMAKE_BUILD_TYPE=Release
cmake --build build --config Release

# Here rather than in the workflow's collect step: the build directory belongs to this
# container's root, and the runner cannot rewrite what is in it.
strip build/libdiffengine_viewer.so
