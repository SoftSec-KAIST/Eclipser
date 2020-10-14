#!/bin/bash

# Tests if Eclipser can solve linear branch conditions in a 32-bit binary.
gcc linear.c -o linear.bin -static -g -m32 || exit 1
rm -rf box
mkdir box
cd box
dotnet ../../build/Eclipser.dll \
  -p ../linear.bin -t 5 -v 1 -o output -f input --arg input --architecture x86
