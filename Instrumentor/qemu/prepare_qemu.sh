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

if [ ! -f "patches-coverage/chatkey.cc" -o ! -f "patches-branch/chatkey.c" ]; then

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
rm -rf "qemu-2.3.0-coverage" || exit 1
rm -rf "qemu-2.3.0-branch" || exit 1
rm -rf "qemu-2.3.0-bbcount" || exit 1
rm -rf "qemu-2.3.0-coverage-x86" || exit 1
rm -rf "qemu-2.3.0-coverage-x64" || exit 1
rm -rf "qemu-2.3.0-branch-x86" || exit 1
rm -rf "qemu-2.3.0-branch-x64" || exit 1
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

cp -r "qemu-2.3.0" "qemu-2.3.0-coverage"
cp -r "qemu-2.3.0" "qemu-2.3.0-branch"
cp -r "qemu-2.3.0" "qemu-2.3.0-bbcount"

### Patch for coverage tracer

echo "[*] Applying patches for coverage..."

patch -p0 <patches-coverage/syscall.diff || exit 1
patch -p0 <patches-coverage/cpu-exec.diff || exit 1
patch -p0 <patches-coverage/exec-all.diff || exit 1
patch -p0 <patches-coverage/translate.diff || exit 1
patch -p0 <patches-coverage/makefile-target.diff || exit 1
cp patches-coverage/chatkey.cc qemu-2.3.0-coverage/
cp patches-coverage/afl-qemu-cpu-inl.h qemu-2.3.0-coverage/
cp patches-coverage/chatkey-utils.h qemu-2.3.0-coverage/

echo "[+] Patching done."

### Patch for branch tracer

echo "[*] Applying patches for branch..."

patch -p0 <patches-branch/cpu-exec.diff || exit 1
patch -p0 <patches-branch/syscall.diff || exit 1
patch -p0 <patches-branch/makefile-target.diff || exit 1
patch -p0 <patches-branch/translate.diff || exit 1
patch -p0 <patches-branch/tcg-target.diff || exit 1
patch -p0 <patches-branch/tcg-op.diff || exit 1
patch -p0 <patches-branch/tcg-opc.diff || exit 1
patch -p0 <patches-branch/tcg.diff || exit 1
patch -p0 <patches-branch/optimize.diff || exit 1
cp patches-branch/chatkey.c qemu-2.3.0-branch/tcg/
cp patches-branch/afl-qemu-cpu-inl.h qemu-2.3.0-branch/

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

cp -r "qemu-2.3.0-coverage" "qemu-2.3.0-coverage-x86"
mv "qemu-2.3.0-coverage" "qemu-2.3.0-coverage-x64"
cp -r "qemu-2.3.0-branch" "qemu-2.3.0-branch-x86"
mv "qemu-2.3.0-branch" "qemu-2.3.0-branch-x64"
cp -r "qemu-2.3.0-bbcount" "qemu-2.3.0-bbcount-x86"
mv "qemu-2.3.0-bbcount" "qemu-2.3.0-bbcount-x64"
