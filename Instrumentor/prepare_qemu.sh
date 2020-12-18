#!/bin/sh
#
# QEMU build script for Eclipser's instrumentation
#
# Modified codes from AFL's QEMU mode (original license below).
# --------------------------------------

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

VERSION="2.10.0"
QEMU_URL="https://download.qemu.org/qemu-${VERSION}.tar.bz2"
QEMU_SHA384="9496d1d209d3a49d67dd83fbcf3f2bf376b7d5f500b0247d813639d104effa79d8a39e64f94d6f9541f6e9d8e3cc574f"

echo "========================================="
echo "QEMU build script for Eclipser"
echo "========================================="
echo

echo "[*] Performing basic sanity checks..."

if [ ! "`uname -s`" = "Linux" ]; then

  echo "[-] Error: QEMU instrumentation is supported only on Linux."
  exit 1

fi

if [ ! -f "patches-coverage/eclipser.c" -o ! -f "patches-branch/eclipser.c" ]; then

  echo "[-] Error: key files not found - wrong working directory?"
  exit 1

fi

for i in libtool wget python2 automake autoconf sha384sum bison iconv; do

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

  echo "[*] Downloading QEMU from the web..."
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

echo "[*] Clean up directories..."

rm -rf "qemu-${VERSION}" || exit 1
rm -rf "qemu-${VERSION}-coverage" || exit 1
rm -rf "qemu-${VERSION}-branch" || exit 1
rm -rf "qemu-${VERSION}-coverage-x86" || exit 1
rm -rf "qemu-${VERSION}-coverage-x64" || exit 1
rm -rf "qemu-${VERSION}-branch-x86" || exit 1
rm -rf "qemu-${VERSION}-branch-x64" || exit 1

echo "[*] Uncompressing archive..."

tar xf "$ARCHIVE" || exit 1

echo "[+] Unpacking successful."

echo "[*] Backup target files of patches-common/ (for later use)"
cp qemu-${VERSION}/configure qemu-${VERSION}/configure.orig
cp qemu-${VERSION}/linux-user/elfload.c qemu-${VERSION}/linux-user/elfload.c.orig
cp qemu-${VERSION}/util/memfd.c qemu-${VERSION}/util/memfd.c.orig
cp qemu-${VERSION}/linux-user/signal.c qemu-${VERSION}/linux-user/signal.c.orig

echo "[*] Applying common patches..."
patch -p0 <patches-common/configure.diff || exit 1
patch -p0 <patches-common/elfload.diff || exit 1
patch -p0 <patches-common/memfd.diff || exit 1
patch -p0 <patches-common/signal.diff || exit 1

cp -r "qemu-${VERSION}" "qemu-${VERSION}-coverage"
cp -r "qemu-${VERSION}" "qemu-${VERSION}-branch"

### Patch for coverage tracer

echo "[*] Applying patches for coverage..."

cp patches-coverage/afl-qemu-cpu-inl.h qemu-${VERSION}-coverage/accel/tcg/
cp patches-coverage/eclipser.c qemu-${VERSION}-coverage/accel/tcg/
patch -p0 <patches-coverage/cpu-exec.diff || exit 1
patch -p0 <patches-coverage/makefile-objs.diff || exit 1
patch -p0 <patches-coverage/syscall.diff || exit 1
patch -p0 <patches-coverage/target-helper.diff || exit 1
patch -p0 <patches-coverage/target-translate.diff || exit 1

echo "[+] Patching done."

cp -r "qemu-${VERSION}-coverage" "qemu-${VERSION}-coverage-x86"
mv "qemu-${VERSION}-coverage" "qemu-${VERSION}-coverage-x64"

### Patch for branch tracer

echo "[*] Applying patches for branch..."

cp patches-branch/afl-qemu-cpu-inl.h qemu-${VERSION}-branch/
cp patches-branch/eclipser.c qemu-${VERSION}-branch/tcg/
patch -p0 <patches-branch/cpu-exec.diff || exit 1
patch -p0 <patches-branch/makefile-target.diff || exit 1
patch -p0 <patches-branch/syscall.diff || exit 1

patch -p0 <patches-branch/optimize.diff || exit 1
patch -p0 <patches-branch/tcg-op.diff || exit 1
patch -p0 <patches-branch/tcg-opc.diff || exit 1
patch -p0 <patches-branch/tcg-target.diff || exit 1
patch -p0 <patches-branch/target-helper.diff || exit 1
patch -p0 <patches-branch/target-translate.diff || exit 1

echo "[+] Patching done."

cp -r "qemu-${VERSION}-branch" "qemu-${VERSION}-branch-x86"
mv "qemu-${VERSION}-branch" "qemu-${VERSION}-branch-x64"
