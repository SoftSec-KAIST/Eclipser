#!/bin/bash

# Grey-box concolic should find file input test case with \x44\x43\x42\x41.
gcc timeout.c -o timeout.bin -static -g || exit 1
rm -rf box
mkdir box
cd box
dotnet ../../build/Eclipser.dll fuzz -p ../timeout.bin -t 5 -v 1 \
  --src auto --maxfilelen 16
