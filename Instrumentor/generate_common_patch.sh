#!/bin/bash

VERSION="2.10.0"

diff -Naur qemu-${VERSION}/configure.orig \
           qemu-${VERSION}/configure \
           > patches-common/configure.diff

diff -Naur qemu-${VERSION}/linux-user/elfload.c.orig \
           qemu-${VERSION}/linux-user/elfload.c \
           > patches-common/elfload.diff

diff -Naur qemu-${VERSION}/util/memfd.c.orig \
           qemu-${VERSION}/util/memfd.c \
           > patches-common/memfd.diff

diff -Naur qemu-${VERSION}/linux-user/signal.c.orig \
           qemu-${VERSION}/linux-user/signal.c \
           > patches-common/signal.diff
