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

##### Common patch

export_common_patch() {
  TARG_DIR=./qemu-2.3.0-$1-$2
  cp qemu-2.3.0/linux-user/elfload.c ./$TARG_DIR/linux-user/
  cp qemu-2.3.0/linux-user/linuxload.c ./$TARG_DIR/linux-user/
  cp qemu-2.3.0/linux-user/signal.c ./$TARG_DIR/linux-user/
  cp qemu-2.3.0/translate-all.c ./$TARG_DIR/
  cp qemu-2.3.0/scripts/texi2pod.pl ./$TARG_DIR/scripts/
  cp qemu-2.3.0/user-exec.c ./$TARG_DIR/
}

export_pathcov_patch() {
  TARG_DIR=./qemu-2.3.0-pathcov-$1
  cp qemu-2.3.0-pathcov/linux-user/syscall.c $TARG_DIR/linux-user/syscall.c
  cp qemu-2.3.0-pathcov/cpu-exec.c $TARG_DIR/cpu-exec.c
  cp qemu-2.3.0-pathcov/target-i386/translate.c $TARG_DIR/target-i386/translate.c
  cp qemu-2.3.0-pathcov/include/exec/exec-all.h ./$TARG_DIR/include/exec/exec-all.h
  cp qemu-2.3.0-pathcov/Makefile.target $TARG_DIR/Makefile.target
  cp qemu-2.3.0-pathcov/chatkey.cc $TARG_DIR/
  cp qemu-2.3.0-pathcov/afl-qemu-cpu-inl.h $TARG_DIR/
  cp qemu-2.3.0-pathcov/chatkey-utils.h $TARG_DIR/
}

export_syscall_patch() {
  TARG_DIR=./qemu-2.3.0-syscall-$1
  cp qemu-2.3.0-syscall/cpu-exec.c $TARG_DIR/cpu-exec.c
  cp qemu-2.3.0-syscall/linux-user/syscall.c $TARG_DIR/linux-user/syscall.c
  cp qemu-2.3.0-syscall/linux-user/Makefile.objs $TARG_DIR/linux-user/Makefile.objs
  cp qemu-2.3.0-syscall/linux-user/chatkey.c $TARG_DIR/linux-user/
}

export_feedback_patch() {
  TARG_DIR=./qemu-2.3.0-feedback-$1
  cp qemu-2.3.0-feedback/cpu-exec.c $TARG_DIR/cpu-exec.c
  cp qemu-2.3.0-feedback/linux-user/syscall.c $TARG_DIR/linux-user/syscall.c
  cp qemu-2.3.0-feedback/Makefile.target $TARG_DIR/Makefile.target
  cp qemu-2.3.0-feedback/target-i386/translate.c $TARG_DIR/target-i386/translate.c
  cp qemu-2.3.0-feedback/tcg/i386/tcg-target.c $TARG_DIR/tcg/i386/tcg-target.c
  cp qemu-2.3.0-feedback/tcg/tcg-op.h $TARG_DIR/tcg/tcg-op.h
  cp qemu-2.3.0-feedback/tcg/tcg-opc.h $TARG_DIR/tcg/tcg-opc.h
  cp qemu-2.3.0-feedback/tcg/tcg.h $TARG_DIR/tcg/tcg.h
  cp qemu-2.3.0-feedback/tcg/optimize.c $TARG_DIR/tcg/optimize.c
  cp qemu-2.3.0-feedback/tcg/chatkey.c $TARG_DIR/tcg/
  cp qemu-2.3.0-feedback/afl-qemu-cpu-inl.h $TARG_DIR/
}

export_bbcount_patch() {
  TARG_DIR=./qemu-2.3.0-bbcount-$1
  cp qemu-2.3.0-bbcount/linux-user/syscall.c $TARG_DIR/linux-user/syscall.c
  cp qemu-2.3.0-bbcount/cpu-exec.c $TARG_DIR/cpu-exec.c
  cp qemu-2.3.0-bbcount/Makefile.target $TARG_DIR/Makefile.target
  cp qemu-2.3.0-bbcount/linux-user/chatkey.cc $TARG_DIR/linux-user/
  cp qemu-2.3.0-bbcount/linux-user/Makefile.objs $TARG_DIR/linux-user/Makefile.objs
  cp qemu-2.3.0-bbcount/linux-user/main.c $TARG_DIR/linux-user/main.c
}

##### Common patch

# Recover
cp qemu-2.3.0/linux-user/elfload.c.orig qemu-2.3.0/linux-user/elfload.c
cp qemu-2.3.0/linux-user/linuxload.c.orig qemu-2.3.0/linux-user/linuxload.c
cp qemu-2.3.0/linux-user/signal.c.orig qemu-2.3.0/linux-user/signal.c
cp qemu-2.3.0/translate-all.c.orig qemu-2.3.0/translate-all.c
cp qemu-2.3.0/scripts/texi2pod.pl.orig qemu-2.3.0/scripts/texi2pod.pl
cp qemu-2.3.0/user-exec.c.orig qemu-2.3.0/user-exec.c

# Patch
patch -p0 <patches-common/elfload.diff || exit 1
patch -p0 <patches-common/linuxload.diff || exit 1
patch -p0 <patches-common/signal.diff || exit 1
patch -p0 <patches-common/translate-all.diff || exit 1
patch -p0 <patches-common/texi2pod.diff || exit 1
patch -p0 <patches-common/user-exec.diff || exit 1

# Export
export_common_patch "pathcov" "x86"
export_common_patch "pathcov" "x64"
export_common_patch "syscall" "x86"
export_common_patch "syscall" "x64"
export_common_patch "feedback" "x86"
export_common_patch "feedback" "x64"
export_common_patch "bbcount" "x86"
export_common_patch "bbcount" "x64"

##### Patch path coverage tracer

# Copy qemu-2.3.0 into qemu-2.3.0-pathcov, to apply patch.
cp -r "qemu-2.3.0" "qemu-2.3.0-pathcov"

# Recover
cp qemu-2.3.0/linux-user/syscall.c qemu-2.3.0-pathcov/linux-user/syscall.c
cp qemu-2.3.0/cpu-exec.c qemu-2.3.0-pathcov/cpu-exec.c
cp qemu-2.3.0/target-i386/translate.c qemu-2.3.0-pathcov/target-i386/translate.c
cp qemu-2.3.0/include/exec/exec-all.h ./qemu-2.3.0-pathcov/include/exec/exec-all.h
cp qemu-2.3.0/Makefile.target qemu-2.3.0-pathcov/Makefile.target

# Patch
patch -p0 <patches-pathcov/syscall.diff || exit 1
patch -p0 <patches-pathcov/cpu-exec.diff || exit 1
patch -p0 <patches-pathcov/exec-all.diff || exit 1
patch -p0 <patches-pathcov/translate.diff || exit 1
patch -p0 <patches-pathcov/makefile-target.diff || exit 1
cp patches-pathcov/chatkey.cc qemu-2.3.0-pathcov/
cp patches-pathcov/afl-qemu-cpu-inl.h qemu-2.3.0-pathcov/
cp patches-pathcov/chatkey-utils.h qemu-2.3.0-pathcov/

export_pathcov_patch "x86"
export_pathcov_patch "x64"

# Cleanup
rm -rf "qemu-2.3.0-pathcov"

##### Patch syscall tracer

# Copy qemu-2.3.0 into qemu-2.3.0-syscall, to apply patch.
cp -r "qemu-2.3.0" "qemu-2.3.0-syscall"

# Recover
cp qemu-2.3.0/cpu-exec.c qemu-2.3.0-syscall/cpu-exec.c
cp qemu-2.3.0/linux-user/syscall.c qemu-2.3.0-syscall/linux-user/syscall.c
cp qemu-2.3.0/linux-user/Makefile.objs qemu-2.3.0-syscall/linux-user/Makefile.objs

# Patch
patch -p0 <patches-syscall/cpu-exec.diff || exit 1
patch -p0 <patches-syscall/syscall.diff || exit 1
patch -p0 <patches-syscall/makefile-objs.diff || exit 1
cp patches-syscall/chatkey.c qemu-2.3.0-syscall/linux-user/

export_syscall_patch "x86"
export_syscall_patch "x64"

# Cleanup
rm -rf "qemu-2.3.0-syscall"

##### Patch feedback tracer

# Copy qemu-2.3.0 into qemu-2.3.0-feedback, to apply patch.
cp -r "qemu-2.3.0" "qemu-2.3.0-feedback"

# Recover
cp qemu-2.3.0/cpu-exec.c qemu-2.3.0-feedback/cpu-exec.c
cp qemu-2.3.0/linux-user/syscall.c qemu-2.3.0-feedback/linux-user/syscall.c
cp qemu-2.3.0/Makefile.target qemu-2.3.0-feedback/Makefile.target
cp qemu-2.3.0/target-i386/translate.c qemu-2.3.0-feedback/target-i386/translate.c
cp qemu-2.3.0/tcg/i386/tcg-target.c qemu-2.3.0-feedback/tcg/i386/tcg-target.c
cp qemu-2.3.0/tcg/tcg-op.h qemu-2.3.0-feedback/tcg/tcg-op.h
cp qemu-2.3.0/tcg/tcg-opc.h qemu-2.3.0-feedback/tcg/tcg-opc.h
cp qemu-2.3.0/tcg/tcg.h qemu-2.3.0-feedback/tcg/tcg.h
cp qemu-2.3.0/tcg/optimize.c qemu-2.3.0-feedback/tcg/optimize.c

# Patch
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

export_feedback_patch "x86"
export_feedback_patch "x64"

# Cleanup
rm -rf "qemu-2.3.0-feedback"

#### Patch the basic block count tracer

# Copy qemu-2.3.0 into qemu-2.3.0-bbcount, to apply patch.
cp -r "qemu-2.3.0" "qemu-2.3.0-bbcount"

# Recover
cp qemu-2.3.0/linux-user/syscall.c qemu-2.3.0-bbcount/linux-user/syscall.c
cp qemu-2.3.0/cpu-exec.c qemu-2.3.0-bbcount/cpu-exec.c
cp qemu-2.3.0/Makefile.target qemu-2.3.0-bbcount/Makefile.target
cp qemu-2.3.0/linux-user/Makefile.objs qemu-2.3.0-bbcount/linux-user/Makefile.objs
cp qemu-2.3.0/linux-user/main.c qemu-2.3.0-bbcount/linux-user/main.c

# Patch
patch -p0 <patches-bbcount/syscall.diff || exit 1
patch -p0 <patches-bbcount/cpu-exec.diff || exit 1
patch -p0 <patches-bbcount/makefile-target.diff || exit 1
patch -p0 <patches-bbcount/makefile-objs.diff || exit 1
patch -p0 <patches-bbcount/main.diff || exit 1
cp patches-bbcount/chatkey.cc qemu-2.3.0-bbcount/linux-user/

export_bbcount_patch "x86"
export_bbcount_patch "x64"

# Cleanup
rm -rf "qemu-2.3.0-bbcount"
