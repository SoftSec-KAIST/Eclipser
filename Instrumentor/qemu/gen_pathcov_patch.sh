#!/bin/bash

cp qemu-2.3.0-pathcov/chatkey.cc ./patches-pathcov/chatkey.cc

cp qemu-2.3.0-pathcov/afl-qemu-cpu-inl.h ./patches-pathcov/afl-qemu-cpu-inl.h

cp qemu-2.3.0-pathcov/chatkey-utils.h ./patches-pathcov/chatkey-utils.h

cp qemu-2.3.0/linux-user/syscall.c qemu-2.3.0-pathcov/linux-user/syscall.c.orig
diff -Naur qemu-2.3.0-pathcov/linux-user/syscall.c.orig qemu-2.3.0-pathcov/linux-user/syscall.c > patches-pathcov/syscall.diff

cp qemu-2.3.0/target-i386/translate.c qemu-2.3.0-pathcov/target-i386/translate.c.orig
diff -Naur qemu-2.3.0-pathcov/target-i386/translate.c.orig qemu-2.3.0-pathcov/target-i386/translate.c > patches-pathcov/translate.diff

cp qemu-2.3.0/cpu-exec.c qemu-2.3.0-pathcov/cpu-exec.c.orig
diff -Naur qemu-2.3.0-pathcov/cpu-exec.c.orig qemu-2.3.0-pathcov/cpu-exec.c > patches-pathcov/cpu-exec.diff

cp qemu-2.3.0/Makefile.target qemu-2.3.0-pathcov/Makefile.target.orig
diff -Naur qemu-2.3.0-pathcov/Makefile.target.orig qemu-2.3.0-pathcov/Makefile.target > patches-pathcov/makefile-target.diff

cp qemu-2.3.0/include/exec/exec-all.h ./qemu-2.3.0-pathcov/include/exec/exec-all.h.orig
diff -Naur qemu-2.3.0-pathcov/include/exec/exec-all.h.orig qemu-2.3.0-pathcov/include/exec/exec-all.h > patches-pathcov/exec-all.diff
