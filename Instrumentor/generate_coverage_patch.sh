#!/bin/bash

VERSION="2.10.0"

cp -r qemu-${VERSION}-coverage-x64 qemu-${VERSION}-coverage

cp qemu-${VERSION}-coverage/accel/tcg/afl-qemu-cpu-inl.h ./patches-coverage/

cp qemu-${VERSION}-coverage/accel/tcg/eclipser.c ./patches-coverage/

cp qemu-${VERSION}/accel/tcg/cpu-exec.c \
   qemu-${VERSION}-coverage/accel/tcg/cpu-exec.c.orig
diff -Naur qemu-${VERSION}-coverage/accel/tcg/cpu-exec.c.orig \
           qemu-${VERSION}-coverage/accel/tcg/cpu-exec.c \
           > patches-coverage/cpu-exec.diff

cp qemu-${VERSION}/accel/tcg/Makefile.objs \
   qemu-${VERSION}-coverage/accel/tcg/Makefile.objs.orig
diff -Naur qemu-${VERSION}-coverage/accel/tcg/Makefile.objs.orig \
           qemu-${VERSION}-coverage/accel/tcg/Makefile.objs \
           > patches-coverage/makefile-objs.diff

cp qemu-${VERSION}/linux-user/syscall.c \
   qemu-${VERSION}-coverage/linux-user/syscall.c.orig
diff -Naur qemu-${VERSION}-coverage/linux-user/syscall.c.orig \
           qemu-${VERSION}-coverage/linux-user/syscall.c \
           > patches-coverage/syscall.diff

rm -rf qemu-${VERSION}-coverage
