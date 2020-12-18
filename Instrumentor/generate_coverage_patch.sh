#!/bin/bash

VERSION="2.10.0"

cp -r qemu-${VERSION}-coverage-x64 qemu-${VERSION}-coverage

cp qemu-${VERSION}-coverage/accel/tcg/afl-qemu-cpu-inl.h ./patches-coverage/

cp qemu-${VERSION}-coverage/accel/tcg/eclipser.c ./patches-coverage/

cp qemu-${VERSION}/accel/tcg/Makefile.objs \
   qemu-${VERSION}-coverage/accel/tcg/Makefile.objs.orig
diff -Naur qemu-${VERSION}-coverage/accel/tcg/Makefile.objs.orig \
           qemu-${VERSION}-coverage/accel/tcg/Makefile.objs \
           > patches-coverage/makefile-objs.diff

cp qemu-${VERSION}/target/i386/translate.c \
   qemu-${VERSION}-coverage/target/i386/translate.c.orig
diff -Naur qemu-${VERSION}-coverage/target/i386/translate.c.orig \
           qemu-${VERSION}-coverage/target/i386/translate.c \
           > patches-coverage/target-translate.diff

rm -rf qemu-${VERSION}-coverage
