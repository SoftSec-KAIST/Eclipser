#!/bin/bash

cp -r qemu-2.3.0-feedback-x64 qemu-2.3.0-feedback

cp qemu-2.3.0-feedback/tcg/chatkey.c ./patches-feedback/chatkey.c

cp qemu-2.3.0-feedback/afl-qemu-cpu-inl.h ./patches-feedback/afl-qemu-cpu-inl.h

cp qemu-2.3.0/target-i386/translate.c qemu-2.3.0-feedback/target-i386/translate.c.orig
diff -Naur qemu-2.3.0-feedback/target-i386/translate.c.orig qemu-2.3.0-feedback/target-i386/translate.c > patches-feedback/translate.diff

cp qemu-2.3.0/cpu-exec.c qemu-2.3.0-feedback/cpu-exec.c.orig
diff -Naur qemu-2.3.0-feedback/cpu-exec.c.orig qemu-2.3.0-feedback/cpu-exec.c > patches-feedback/cpu-exec.diff

cp qemu-2.3.0/linux-user/syscall.c qemu-2.3.0-feedback/linux-user/syscall.c.orig
diff -Naur qemu-2.3.0-feedback/linux-user/syscall.c.orig qemu-2.3.0-feedback/linux-user/syscall.c > patches-feedback/syscall.diff

cp qemu-2.3.0/Makefile.target qemu-2.3.0-feedback/Makefile.target.orig
diff -Naur qemu-2.3.0-feedback/Makefile.target.orig qemu-2.3.0-feedback/Makefile.target > patches-feedback/makefile-target.diff

cp qemu-2.3.0/tcg/tcg-opc.h qemu-2.3.0-feedback/tcg/tcg-opc.h.orig
diff -Naur qemu-2.3.0-feedback/tcg/tcg-opc.h.orig qemu-2.3.0-feedback/tcg/tcg-opc.h > patches-feedback/tcg-opc.diff

cp qemu-2.3.0/tcg/tcg-op.h qemu-2.3.0-feedback/tcg/tcg-op.h.orig
diff -Naur qemu-2.3.0-feedback/tcg/tcg-op.h.orig qemu-2.3.0-feedback/tcg/tcg-op.h > patches-feedback/tcg-op.diff

cp qemu-2.3.0/tcg/i386/tcg-target.c qemu-2.3.0-feedback/tcg/i386/tcg-target.c.orig
diff -Naur qemu-2.3.0-feedback/tcg/i386/tcg-target.c.orig qemu-2.3.0-feedback/tcg/i386/tcg-target.c > patches-feedback/tcg-target.diff

cp qemu-2.3.0/tcg/tcg.h qemu-2.3.0-feedback/tcg/tcg.h.orig
diff -Naur qemu-2.3.0-feedback/tcg/tcg.h.orig qemu-2.3.0-feedback/tcg/tcg.h > patches-feedback/tcg.diff

cp qemu-2.3.0/tcg/optimize.c qemu-2.3.0-feedback/tcg/optimize.c.orig
diff -Naur qemu-2.3.0-feedback/tcg/optimize.c.orig qemu-2.3.0-feedback/tcg/optimize.c > patches-feedback/optimize.diff
