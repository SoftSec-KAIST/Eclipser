#!/bin/bash

cp -r qemu-2.3.0-syscall-x64 qemu-2.3.0-syscall

cp qemu-2.3.0-syscall/linux-user/chatkey.c ./patches-syscall/chatkey.c

cp qemu-2.3.0/linux-user/syscall.c qemu-2.3.0-syscall/linux-user/syscall.c.orig
diff -Naur qemu-2.3.0-syscall/linux-user/syscall.c.orig qemu-2.3.0-syscall/linux-user/syscall.c > patches-syscall/syscall.diff

cp qemu-2.3.0/cpu-exec.c qemu-2.3.0-syscall/cpu-exec.c.orig
diff -Naur qemu-2.3.0-syscall/cpu-exec.c.orig qemu-2.3.0-syscall/cpu-exec.c > patches-syscall/cpu-exec.diff

cp qemu-2.3.0/linux-user/Makefile.objs qemu-2.3.0-syscall/linux-user/Makefile.objs.orig
diff -Naur qemu-2.3.0-syscall/linux-user/Makefile.objs.orig qemu-2.3.0-syscall/linux-user/Makefile.objs > patches-syscall/makefile-objs.diff
