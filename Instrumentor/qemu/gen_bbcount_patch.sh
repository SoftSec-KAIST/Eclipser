#!/bin/bash

cp -r qemu-2.3.0-bbcount-x64 qemu-2.3.0-bbcount

cp qemu-2.3.0-bbcount/chatkey.cc ./patches-bbcount/chatkey.cc

cp qemu-2.3.0/linux-user/syscall.c qemu-2.3.0-bbcount/linux-user/syscall.c.orig
diff -Naur qemu-2.3.0-bbcount/linux-user/syscall.c.orig qemu-2.3.0-bbcount/linux-user/syscall.c > patches-bbcount/syscall.diff

cp qemu-2.3.0/cpu-exec.c qemu-2.3.0-bbcount/cpu-exec.c.orig
diff -Naur qemu-2.3.0-bbcount/cpu-exec.c.orig qemu-2.3.0-bbcount/cpu-exec.c > patches-bbcount/cpu-exec.diff

cp qemu-2.3.0/Makefile.target qemu-2.3.0-bbcount/Makefile.target.orig
diff -Naur qemu-2.3.0-bbcount/Makefile.target.orig qemu-2.3.0-bbcount/Makefile.target > patches-bbcount/makefile-target.diff

cp qemu-2.3.0/linux-user/Makefile.objs qemu-2.3.0-bbcount/linux-user/Makefile.objs.orig
diff -Naur qemu-2.3.0-bbcount/linux-user/Makefile.objs.orig qemu-2.3.0-bbcount/linux-user/Makefile.objs > patches-bbcount/makefile-objs.diff

cp qemu-2.3.0/linux-user/main.c qemu-2.3.0-bbcount/linux-user/main.c.orig
diff -Naur qemu-2.3.0-bbcount/linux-user/main.c.orig qemu-2.3.0-bbcount/linux-user/main.c > patches-bbcount/main.diff

rm -rf qemu-2.3.0 qemu-2.3.0-bbcount
