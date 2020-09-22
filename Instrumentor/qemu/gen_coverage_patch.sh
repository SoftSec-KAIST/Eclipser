#!/bin/bash

cp qemu-2.3.0-coverage/chatkey.cc ./patches-coverage/chatkey.cc

cp qemu-2.3.0-coverage/afl-qemu-cpu-inl.h ./patches-coverage/afl-qemu-cpu-inl.h

cp qemu-2.3.0-coverage/chatkey-utils.h ./patches-coverage/chatkey-utils.h

cp qemu-2.3.0/linux-user/syscall.c qemu-2.3.0-coverage/linux-user/syscall.c.orig
diff -Naur qemu-2.3.0-coverage/linux-user/syscall.c.orig qemu-2.3.0-coverage/linux-user/syscall.c > patches-coverage/syscall.diff

cp qemu-2.3.0/target-i386/translate.c qemu-2.3.0-coverage/target-i386/translate.c.orig
diff -Naur qemu-2.3.0-coverage/target-i386/translate.c.orig qemu-2.3.0-coverage/target-i386/translate.c > patches-coverage/translate.diff

cp qemu-2.3.0/cpu-exec.c qemu-2.3.0-coverage/cpu-exec.c.orig
diff -Naur qemu-2.3.0-coverage/cpu-exec.c.orig qemu-2.3.0-coverage/cpu-exec.c > patches-coverage/cpu-exec.diff

cp qemu-2.3.0/Makefile.target qemu-2.3.0-coverage/Makefile.target.orig
diff -Naur qemu-2.3.0-coverage/Makefile.target.orig qemu-2.3.0-coverage/Makefile.target > patches-coverage/makefile-target.diff

cp qemu-2.3.0/include/exec/exec-all.h ./qemu-2.3.0-coverage/include/exec/exec-all.h.orig
diff -Naur qemu-2.3.0-coverage/include/exec/exec-all.h.orig qemu-2.3.0-coverage/include/exec/exec-all.h > patches-coverage/exec-all.diff
