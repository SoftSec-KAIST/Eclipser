#!/bin/bash

cp qemu-2.3.0-branch/tcg/chatkey.c ./patches-branch/chatkey.c

cp qemu-2.3.0-branch/afl-qemu-cpu-inl.h ./patches-branch/afl-qemu-cpu-inl.h

cp qemu-2.3.0/target-i386/translate.c qemu-2.3.0-branch/target-i386/translate.c.orig
diff -Naur qemu-2.3.0-branch/target-i386/translate.c.orig qemu-2.3.0-branch/target-i386/translate.c > patches-branch/translate.diff

cp qemu-2.3.0/cpu-exec.c qemu-2.3.0-branch/cpu-exec.c.orig
diff -Naur qemu-2.3.0-branch/cpu-exec.c.orig qemu-2.3.0-branch/cpu-exec.c > patches-branch/cpu-exec.diff

cp qemu-2.3.0/linux-user/syscall.c qemu-2.3.0-branch/linux-user/syscall.c.orig
diff -Naur qemu-2.3.0-branch/linux-user/syscall.c.orig qemu-2.3.0-branch/linux-user/syscall.c > patches-branch/syscall.diff

cp qemu-2.3.0/Makefile.target qemu-2.3.0-branch/Makefile.target.orig
diff -Naur qemu-2.3.0-branch/Makefile.target.orig qemu-2.3.0-branch/Makefile.target > patches-branch/makefile-target.diff

cp qemu-2.3.0/tcg/tcg-opc.h qemu-2.3.0-branch/tcg/tcg-opc.h.orig
diff -Naur qemu-2.3.0-branch/tcg/tcg-opc.h.orig qemu-2.3.0-branch/tcg/tcg-opc.h > patches-branch/tcg-opc.diff

cp qemu-2.3.0/tcg/tcg-op.h qemu-2.3.0-branch/tcg/tcg-op.h.orig
diff -Naur qemu-2.3.0-branch/tcg/tcg-op.h.orig qemu-2.3.0-branch/tcg/tcg-op.h > patches-branch/tcg-op.diff

cp qemu-2.3.0/tcg/i386/tcg-target.c qemu-2.3.0-branch/tcg/i386/tcg-target.c.orig
diff -Naur qemu-2.3.0-branch/tcg/i386/tcg-target.c.orig qemu-2.3.0-branch/tcg/i386/tcg-target.c > patches-branch/tcg-target.diff

cp qemu-2.3.0/tcg/tcg.h qemu-2.3.0-branch/tcg/tcg.h.orig
diff -Naur qemu-2.3.0-branch/tcg/tcg.h.orig qemu-2.3.0-branch/tcg/tcg.h > patches-branch/tcg.diff

cp qemu-2.3.0/tcg/optimize.c qemu-2.3.0-branch/tcg/optimize.c.orig
diff -Naur qemu-2.3.0-branch/tcg/optimize.c.orig qemu-2.3.0-branch/tcg/optimize.c > patches-branch/optimize.diff
