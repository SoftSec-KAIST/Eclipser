#!/bin/sh
#
# QEMU rebuild script for Eclipser's instrumentation

VERSION="2.10.0"

##### Common patch

export_common_patch() {
  TARG_DIR=./qemu-${VERSION}-$1-$2
  cp qemu-${VERSION}/configure ./$TARG_DIR/
  cp qemu-${VERSION}/linux-user/elfload.c ./$TARG_DIR/linux-user/
  cp qemu-${VERSION}/util/memfd.c ./$TARG_DIR/util/
  cp qemu-${VERSION}/linux-user/signal.c ./$TARG_DIR/linux-user/
}

export_coverage_patch() {
  TARG_DIR=./qemu-${VERSION}-coverage-$1
  cp qemu-${VERSION}-coverage/accel/tcg/afl-qemu-cpu-inl.h $TARG_DIR/accel/tcg/
  cp qemu-${VERSION}-coverage/accel/tcg/eclipser.c $TARG_DIR/accel/tcg/
  cp qemu-${VERSION}-coverage/accel/tcg/cpu-exec.c $TARG_DIR/accel/tcg/cpu-exec.c
  cp qemu-${VERSION}-coverage/accel/tcg/Makefile.objs $TARG_DIR/accel/tcg/Makefile.objs
  cp qemu-${VERSION}-coverage/linux-user/syscall.c $TARG_DIR/linux-user/syscall.c
}

export_branch_patch() {
  TARG_DIR=./qemu-${VERSION}-branch-$1
  cp qemu-${VERSION}-branch/afl-qemu-cpu-inl.h $TARG_DIR/
  cp qemu-${VERSION}-branch/tcg/eclipser.c $TARG_DIR/tcg/
  cp qemu-${VERSION}-branch/accel/tcg/cpu-exec.c $TARG_DIR/accel/tcg/cpu-exec.c
  cp qemu-${VERSION}-branch/Makefile.target $TARG_DIR/Makefile.target
  cp qemu-${VERSION}-branch/linux-user/syscall.c $TARG_DIR/linux-user/syscall.c

  cp qemu-${VERSION}-branch/tcg/optimize.c $TARG_DIR/tcg/optimize.c
  cp qemu-${VERSION}-branch/tcg/tcg-op.h $TARG_DIR/tcg/tcg-op.h
  cp qemu-${VERSION}-branch/tcg/tcg-opc.h $TARG_DIR/tcg/tcg-opc.h
  cp qemu-${VERSION}-branch/tcg/i386/tcg-target.inc.c  $TARG_DIR/tcg/i386/tcg-target.inc.c
  cp qemu-${VERSION}-branch/target/i386/translate.c $TARG_DIR/target/i386/translate.c
}

export_bbcount_patch() {
  TARG_DIR=./qemu-${VERSION}-bbcount-$1
  cp qemu-${VERSION}-bbcount/eclipser.cc $TARG_DIR/
  cp qemu-${VERSION}-bbcount/accel/tcg/cpu-exec.c $TARG_DIR/accel/tcg/cpu-exec.c
  cp qemu-${VERSION}-bbcount/Makefile.target $TARG_DIR/Makefile.target
  cp qemu-${VERSION}-bbcount/linux-user/syscall.c $TARG_DIR/linux-user/syscall.c
}

##### Common patch

# Recover original files.
cp qemu-${VERSION}/configure.orig qemu-${VERSION}/configure
cp qemu-${VERSION}/linux-user/elfload.c.orig qemu-${VERSION}/linux-user/elfload.c
cp qemu-${VERSION}/util/memfd.c.orig qemu-${VERSION}/util/memfd.c
cp qemu-${VERSION}/linux-user/signal.c.orig qemu-${VERSION}/linux-user/signal.c

# Patch
patch -p0 <patches-common/configure.diff || exit 1
patch -p0 <patches-common/elfload.diff || exit 1
patch -p0 <patches-common/memfd.diff || exit 1
patch -p0 <patches-common/signal.diff || exit 1

# Export
export_common_patch "coverage" "x86"
export_common_patch "coverage" "x64"
export_common_patch "branch" "x86"
export_common_patch "branch" "x64"
export_common_patch "bbcount" "x86"
export_common_patch "bbcount" "x64"

##### Patch coverage tracer

# Copy qemu-${VERSION} into qemu-${VERSION}-coverage, to apply patch.
cp -r "qemu-${VERSION}" "qemu-${VERSION}-coverage"

# Patch
cp patches-coverage/afl-qemu-cpu-inl.h qemu-${VERSION}-coverage/accel/tcg/
cp patches-coverage/eclipser.c qemu-${VERSION}-coverage/accel/tcg/
patch -p0 <patches-coverage/cpu-exec.diff || exit 1
patch -p0 <patches-coverage/makefile-objs.diff || exit 1
patch -p0 <patches-coverage/syscall.diff || exit 1

export_coverage_patch "x86"
export_coverage_patch "x64"

# Cleanup
rm -rf "qemu-${VERSION}-coverage"

##### Patch branch tracer

# Copy qemu-${VERSION} into qemu-${VERSION}-branch, to apply patch.
cp -r "qemu-${VERSION}" "qemu-${VERSION}-branch"

# Patch
cp patches-branch/afl-qemu-cpu-inl.h qemu-${VERSION}-branch/
cp patches-branch/eclipser.c qemu-${VERSION}-branch/tcg/
patch -p0 <patches-branch/cpu-exec.diff || exit 1
patch -p0 <patches-branch/makefile-target.diff || exit 1
patch -p0 <patches-branch/syscall.diff || exit 1

patch -p0 <patches-branch/optimize.diff || exit 1
patch -p0 <patches-branch/tcg-op.diff || exit 1
patch -p0 <patches-branch/tcg-opc.diff || exit 1
patch -p0 <patches-branch/tcg-target.diff || exit 1
patch -p0 <patches-branch/translate.diff || exit 1

export_branch_patch "x86"
export_branch_patch "x64"

# Cleanup
rm -rf "qemu-${VERSION}-branch"

### Patch the basic block count tracer

# Copy qemu-${VERSION} into qemu-${VERSION}-bbcount, to apply patch.
cp -r "qemu-${VERSION}" "qemu-${VERSION}-bbcount"

# Patch
cp patches-bbcount/eclipser.cc qemu-${VERSION}-bbcount/
patch -p0 <patches-bbcount/cpu-exec.diff || exit 1
patch -p0 <patches-bbcount/makefile-target.diff || exit 1
patch -p0 <patches-bbcount/syscall.diff || exit 1

export_bbcount_patch "x86"
export_bbcount_patch "x64"

# Cleanup
rm -rf "qemu-${VERSION}-bbcount"
