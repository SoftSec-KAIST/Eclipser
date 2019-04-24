#!/bin/sh
#
# QEMU build script for Eclipser's instrumentation
#
# Modified codes from AFL's QEMU mode (original license below).
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
      --python=python2 --enable-guest-base --disable-gtk --disable-sdl --disable-vnc \
      --target-list="${CPU_TARGET}-linux-user" || exit 1

    echo "[+] Configuration complete."

    echo "[*] Attempting to build QEMU (fingers crossed!)..."

    make || exit 1

    echo "[+] Build process successful!"

    echo "[*] Copying binary..."
    cp -f "${CPU_TARGET}-linux-user/qemu-${CPU_TARGET}" "../qemu-trace" || exit 1
    cd ..
}

QEMU_URL="https://download.qemu.org/qemu-2.3.0.tar.bz2"
QEMU_SHA384="7a0f0c900f7e2048463cc32ff3e904965ab466c8428847400a0f2dcfe458108a68012c4fddb2a7e7c822b4fd1a49639b"

echo "========================================="
echo "Chatkey instrumentation QEMU build script"
echo "========================================="
echo

echo "[*] Performing basic sanity checks..."

if [ ! "`uname -s`" = "Linux" ]; then

  echo "[-] Error: QEMU instrumentation is supported only on Linux."
  exit 1

fi

if [ ! -f "patches-pathcov/chatkey.cc" -o ! -f "patches-syscall/chatkey.c" -o ! -f "patches-feedback/chatkey.c" ]; then

  echo "[-] Error: key files not found - wrong working directory?"
  exit 1

fi

for i in libtool wget python automake autoconf sha384sum bison iconv; do

  T=`which "$i" 2>/dev/null`

  if [ "$T" = "" ]; then

    echo "[-] Error: '$i' not found, please install first."
    exit 1

  fi

done

if [ ! -d "/usr/include/glib-2.0/" -a ! -d "/usr/local/include/glib-2.0/" ]; then

  echo "[-] Error: devel version of 'glib2' not found, please install first."
  exit 1

fi

echo "[+] All checks passed!"

ARCHIVE="`basename -- "$QEMU_URL"`"

CKSUM=`sha384sum -- "$ARCHIVE" 2>/dev/null | cut -d' ' -f1`

if [ ! "$CKSUM" = "$QEMU_SHA384" ]; then

  echo "[*] Downloading QEMU 2.3.0 from the web..."
  rm -f "$ARCHIVE"
  wget -O "$ARCHIVE" -- "$QEMU_URL" || exit 1

  CKSUM=`sha384sum -- "$ARCHIVE" 2>/dev/null | cut -d' ' -f1`

fi

if [ "$CKSUM" = "$QEMU_SHA384" ]; then

  echo "[+] Cryptographic signature on $ARCHIVE checks out."

else

  echo "[-] Error: signature mismatch on $ARCHIVE (perhaps download error?)."
  exit 1

fi

echo "[*] Uncompressing archive (this will take a while)..."

rm -rf "qemu-2.3.0" || exit 1
rm -rf "qemu-2.3.0-pathcov" || exit 1
rm -rf "qemu-2.3.0-syscall" || exit 1
rm -rf "qemu-2.3.0-feedback" || exit 1
rm -rf "qemu-2.3.0-bbcount" || exit 1
rm -rf "qemu-2.3.0-pathcov-x86" || exit 1
rm -rf "qemu-2.3.0-pathcov-x64" || exit 1
rm -rf "qemu-2.3.0-syscall-x86" || exit 1
rm -rf "qemu-2.3.0-syscall-x64" || exit 1
rm -rf "qemu-2.3.0-feedback-x86" || exit 1
rm -rf "qemu-2.3.0-feedback-x64" || exit 1
rm -rf "qemu-2.3.0-bbcount-x86" || exit 1
rm -rf "qemu-2.3.0-bbcount-x64" || exit 1
tar xf "$ARCHIVE" || exit 1

echo "[+] Unpacking successful."

echo "[*] Backup target files of patches-common/ (for later use)"
cp qemu-2.3.0/linux-user/elfload.c qemu-2.3.0/linux-user/elfload.c.orig
cp qemu-2.3.0/linux-user/linuxload.c qemu-2.3.0/linux-user/linuxload.c.orig
cp qemu-2.3.0/linux-user/signal.c qemu-2.3.0/linux-user/signal.c.orig
cp qemu-2.3.0/translate-all.c qemu-2.3.0/translate-all.c.orig
cp qemu-2.3.0/scripts/texi2pod.pl qemu-2.3.0/scripts/texi2pod.pl.orig
cp qemu-2.3.0/user-exec.c qemu-2.3.0/user-exec.c.orig
cp qemu-2.3.0/configure qemu-2.3.0/configure.orig
cp qemu-2.3.0/include/sysemu/os-posix.h qemu-2.3.0/include/sysemu/os-posix.h.orig

echo "[*] Applying common patches..."
patch -p0 <patches-common/elfload.diff || exit 1
patch -p0 <patches-common/linuxload.diff || exit 1
patch -p0 <patches-common/signal.diff || exit 1
patch -p0 <patches-common/translate-all.diff || exit 1
patch -p0 <patches-common/texi2pod.diff || exit 1
patch -p0 <patches-common/user-exec.diff || exit 1
patch -p0 <patches-common/os-posix.diff || exit 1
patch -p0 <patches-common/configure.diff || exit 1

cp -r "qemu-2.3.0" "qemu-2.3.0-pathcov"
cp -r "qemu-2.3.0" "qemu-2.3.0-syscall"
cp -r "qemu-2.3.0" "qemu-2.3.0-feedback"
cp -r "qemu-2.3.0" "qemu-2.3.0-bbcount"

### Patch for pathcov tracer

echo "[*] Applying patches for pathcov..."

patch -p0 <patches-pathcov/syscall.diff || exit 1
patch -p0 <patches-pathcov/cpu-exec.diff || exit 1
patch -p0 <patches-pathcov/exec-all.diff || exit 1
patch -p0 <patches-pathcov/translate.diff || exit 1
patch -p0 <patches-pathcov/makefile-target.diff || exit 1
cp patches-pathcov/chatkey.cc qemu-2.3.0-pathcov/
cp patches-pathcov/afl-qemu-cpu-inl.h qemu-2.3.0-pathcov/
cp patches-pathcov/chatkey-utils.h qemu-2.3.0-pathcov/

echo "[+] Patching done."

### Patch for syscall tracer

echo "[*] Applying patches for syscall..."

patch -p0 <patches-syscall/cpu-exec.diff || exit 1
patch -p0 <patches-syscall/syscall.diff || exit 1
patch -p0 <patches-syscall/makefile-objs.diff || exit 1
cp patches-syscall/chatkey.c qemu-2.3.0-syscall/linux-user/

echo "[+] Patching done."

### Patch for feedback tracer

echo "[*] Applying patches for feedback..."

patch -p0 <patches-feedback/cpu-exec.diff || exit 1
patch -p0 <patches-feedback/syscall.diff || exit 1
patch -p0 <patches-feedback/makefile-target.diff || exit 1
patch -p0 <patches-feedback/translate.diff || exit 1
patch -p0 <patches-feedback/tcg-target.diff || exit 1
patch -p0 <patches-feedback/tcg-op.diff || exit 1
patch -p0 <patches-feedback/tcg-opc.diff || exit 1
patch -p0 <patches-feedback/tcg.diff || exit 1
patch -p0 <patches-feedback/optimize.diff || exit 1
cp patches-feedback/chatkey.c qemu-2.3.0-feedback/tcg/
cp patches-feedback/afl-qemu-cpu-inl.h qemu-2.3.0-feedback/

echo "[+] Patching done."

### Patch for basic block count tracer

echo "[*] Applying patches for bbcount..."

patch -p0 <patches-bbcount/syscall.diff || exit 1
patch -p0 <patches-bbcount/cpu-exec.diff || exit 1
patch -p0 <patches-bbcount/makefile-target.diff || exit 1
patch -p0 <patches-bbcount/makefile-objs.diff || exit 1
patch -p0 <patches-bbcount/main.diff || exit 1
cp patches-bbcount/chatkey.cc qemu-2.3.0-bbcount/linux-user/
echo "[+] Patching done."

### Copy directories, one for x86 and the other for x64

cp -r "qemu-2.3.0-pathcov" "qemu-2.3.0-pathcov-x86"
mv "qemu-2.3.0-pathcov" "qemu-2.3.0-pathcov-x64"
cp -r "qemu-2.3.0-syscall" "qemu-2.3.0-syscall-x86"
mv "qemu-2.3.0-syscall" "qemu-2.3.0-syscall-x64"
cp -r "qemu-2.3.0-feedback" "qemu-2.3.0-feedback-x86"
mv "qemu-2.3.0-feedback" "qemu-2.3.0-feedback-x64"
cp -r "qemu-2.3.0-bbcount" "qemu-2.3.0-bbcount-x86"
mv "qemu-2.3.0-bbcount" "qemu-2.3.0-bbcount-x64"

### Build QEMU tracers

build_qemu pathcov x86
mv "./qemu-trace" "../../build/qemu-trace-pathcov-x86" || exit 1
echo "[+] Successfully created 'qemu-trace-pathcov-x86'."

build_qemu pathcov x64
mv "./qemu-trace" "../../build/qemu-trace-pathcov-x64" || exit 1
echo "[+] Successfully created 'qemu-trace-pathcov-x64'."

build_qemu syscall x86
mv "./qemu-trace" "../../build/qemu-trace-syscall-x86" || exit 1
echo "[+] Successfully created 'qemu-trace-syscall-x86'."

build_qemu syscall x64
mv "./qemu-trace" "../../build/qemu-trace-syscall-x64" || exit 1
echo "[+] Successfully created 'qemu-trace-syscall-x64'."

build_qemu feedback x86
mv "./qemu-trace" "../../build/qemu-trace-feedback-x86" || exit 1
echo "[+] Successfully created 'qemu-trace-feedback-x86'."

build_qemu feedback x64
mv "./qemu-trace" "../../build/qemu-trace-feedback-x64" || exit 1
echo "[+] Successfully created 'qemu-trace-feedback-x64'."

build_qemu bbcount x86
mv "./qemu-trace" "../../build/qemu-trace-bbcount-x86" || exit 1
echo "[+] Successfully created 'qemu-trace-bbcount-x86'."

build_qemu bbcount x64
mv "./qemu-trace" "../../build/qemu-trace-bbcount-x64" || exit 1
echo "[+] Successfully created 'qemu-trace-bbcount-x64'."

exit 0
