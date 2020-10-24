#!/bin/bash

# Tests if Eclipser can solve monotonic branch conditions. Eclipser should be
# able to find test cases containing \x41\x42, \x61\x62, "Good!", and "Bad!".
gcc monoton.c -o monoton.bin -static -g || exit 1
rm -rf box
mkdir box
cd box
dotnet ../../build/Eclipser.dll \
  -p ../monoton.bin -t 10 -v 2 -o output -f input --arg input
