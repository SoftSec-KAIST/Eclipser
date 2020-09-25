#!/bin/bash

VERSION="2.10.0"

cp -r qemu-${VERSION}-bbcount-x64 qemu-${VERSION}-bbcount

cp qemu-${VERSION}-bbcount/chatkey.cc ./patches-bbcount/

cp qemu-${VERSION}/accel/tcg/cpu-exec.c \
   qemu-${VERSION}-bbcount/accel/tcg/cpu-exec.c.orig
diff -Naur qemu-${VERSION}-bbcount/accel/tcg/cpu-exec.c.orig \
           qemu-${VERSION}-bbcount/accel/tcg/cpu-exec.c \
           > patches-bbcount/cpu-exec.diff

cp qemu-${VERSION}/Makefile.target \
   qemu-${VERSION}-bbcount/Makefile.target.orig
diff -Naur qemu-${VERSION}-bbcount/Makefile.target.orig \
           qemu-${VERSION}-bbcount/Makefile.target \
           > patches-bbcount/makefile-target.diff

cp qemu-${VERSION}/linux-user/syscall.c \
   qemu-${VERSION}-bbcount/linux-user/syscall.c.orig
diff -Naur qemu-${VERSION}-bbcount/linux-user/syscall.c.orig \
           qemu-${VERSION}-bbcount/linux-user/syscall.c \
           > patches-bbcount/syscall.diff

rm -rf qemu-${VERSION}-bbcount
