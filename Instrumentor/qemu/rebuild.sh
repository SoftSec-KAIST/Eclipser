#!/bin/sh
#
# Chatkey QEMU build script
#
# Modified codes are Copyright 2016 KAIST SoftSec.
#
# Copied from afl-fuzz QEMU mode
# --------------------------------------
#
# Written by Andrew Griffiths <agriffiths@google.com> and
#            Michal Zalewski <lcamtuf@google.com>
#
# Copyright 2015, 2016 Google Inc. All rights reserved.
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at:
#
#   http://www.apache.org/licenses/LICENSE-2.0
#

build_qemu () {
    if [ $2 = "x86" ]; then
        CPU_TARGET="i386"
    elif [ $2 = "x64" ]; then
        CPU_TARGET="x86_64"
    else
        echo "Invalid CPU architecture provided"
        exit 0
    fi

    echo "[*] Configuring QEMU for $CPU_TARGET..."

    cd qemu-2.3.0-$1-$2 || exit 1

    CFLAGS="-O3" ./configure --disable-system --enable-linux-user \
      --enable-guest-base --disable-gtk --disable-sdl --disable-vnc \
      --target-list="${CPU_TARGET}-linux-user" || exit 1

    echo "[+] Configuration complete."

    echo "[*] Attempting to build QEMU (fingers crossed!)..."

    make || exit 1

    echo "[+] Build process successful!"

    echo "[*] Copying binary..."
    cp -f "${CPU_TARGET}-linux-user/qemu-${CPU_TARGET}" "../qemu-trace" || exit 1
    cd ..
}

echo "========================================="
echo "Chatkey instrumentation QEMU build script"
echo "========================================="


echo "[+] Sanity checking and patching omitted."

build_qemu coverage x86
mv "./qemu-trace" "../../build/qemu-trace-coverage-x86" || exit 1
echo "[+] Successfully created 'qemu-trace-coverage-x86'."

build_qemu coverage x64
mv "./qemu-trace" "../../build/qemu-trace-coverage-x64" || exit 1
echo "[+] Successfully created 'qemu-trace-coverage-x64'."

build_qemu branch x86
mv "./qemu-trace" "../../build/qemu-trace-branch-x86" || exit 1
echo "[+] Successfully created 'qemu-trace-branch-x86'."

build_qemu branch x64
mv "./qemu-trace" "../../build/qemu-trace-branch-x64" || exit 1
echo "[+] Successfully created 'qemu-trace-branch-x64'."

build_qemu bbcount x86
mv "./qemu-trace" "../../build/qemu-trace-bbcount-x86" || exit 1
echo "[+] Successfully created 'qemu-trace-bbcount-x86'."

build_qemu bbcount x64
mv "./qemu-trace" "../../build/qemu-trace-bbcount-x64" || exit 1
echo "[+] Successfully created 'qemu-trace-bbcount-x64'."

exit 0
