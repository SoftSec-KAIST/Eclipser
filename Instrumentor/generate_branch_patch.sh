#!/bin/bash

VERSION="2.10.0"

cp -r qemu-${VERSION}-branch-x64 qemu-${VERSION}-branch

cp qemu-${VERSION}-branch/afl-qemu-cpu-inl.h ./patches-branch/afl-qemu-cpu-inl.h

cp qemu-${VERSION}-branch/tcg/eclipser.c ./patches-branch/eclipser.c

cp qemu-${VERSION}/accel/tcg/cpu-exec.c \
   qemu-${VERSION}-branch/accel/tcg/cpu-exec.c.orig
diff -Naur qemu-${VERSION}-branch/accel/tcg/cpu-exec.c.orig \
           qemu-${VERSION}-branch/accel/tcg/cpu-exec.c \
           > patches-branch/cpu-exec.diff

cp qemu-${VERSION}/Makefile.target \
   qemu-${VERSION}-branch/Makefile.target.orig
diff -Naur qemu-${VERSION}-branch/Makefile.target.orig \
           qemu-${VERSION}-branch/Makefile.target \
           > patches-branch/makefile-target.diff

cp qemu-${VERSION}/linux-user/syscall.c \
   qemu-${VERSION}-branch/linux-user/syscall.c.orig
diff -Naur qemu-${VERSION}-branch/linux-user/syscall.c.orig \
           qemu-${VERSION}-branch/linux-user/syscall.c \
           > patches-branch/syscall.diff

cp qemu-${VERSION}/tcg/optimize.c \
   qemu-${VERSION}-branch/tcg/optimize.c.orig
diff -Naur qemu-${VERSION}-branch/tcg/optimize.c.orig \
           qemu-${VERSION}-branch/tcg/optimize.c \
           > patches-branch/optimize.diff

cp qemu-${VERSION}/tcg/tcg-op.h \
   qemu-${VERSION}-branch/tcg/tcg-op.h.orig
diff -Naur qemu-${VERSION}-branch/tcg/tcg-op.h.orig \
           qemu-${VERSION}-branch/tcg/tcg-op.h \
           > patches-branch/tcg-op.diff

cp qemu-${VERSION}/tcg/tcg-opc.h \
   qemu-${VERSION}-branch/tcg/tcg-opc.h.orig
diff -Naur qemu-${VERSION}-branch/tcg/tcg-opc.h.orig \
           qemu-${VERSION}-branch/tcg/tcg-opc.h \
           > patches-branch/tcg-opc.diff

cp qemu-${VERSION}/tcg/i386/tcg-target.inc.c \
   qemu-${VERSION}-branch/tcg/i386/tcg-target.inc.c.orig
diff -Naur qemu-${VERSION}-branch/tcg/i386/tcg-target.inc.c.orig \
           qemu-${VERSION}-branch/tcg/i386/tcg-target.inc.c \
           > patches-branch/tcg-target.diff

cp qemu-${VERSION}/target/i386/translate.c \
   qemu-${VERSION}-branch/target/i386/translate.c.orig
diff -Naur qemu-${VERSION}-branch/target/i386/translate.c.orig \
           qemu-${VERSION}-branch/target/i386/translate.c \
           > patches-branch/translate.diff

rm -rf qemu-${VERSION}-branch
