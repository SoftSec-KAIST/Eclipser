#!/bin/bash

# Grey-box concolic should find file input test case with \x64\x63\x62\x61.
gcc loop.c -o loop.bin -static -g || exit 1
rm -rf box
mkdir box
cd box
dotnet ../../build/Eclipser.dll fuzz -p ../loop.bin -t 60 -v 1 -o output \
  --src auto --maxfilelen 16 --nsolve 10
