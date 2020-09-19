#!/bin/bash

# Grey-box concolic should find test cases that have \x41\x42 and "Good!".
gcc monoton.c -o monoton.bin -static -g || exit 1
rm -rf box
mkdir box
cd box
dotnet ../../build/Eclipser.dll \
  -p ../monoton.bin -t 10 -v 1 -o output -f input --arg input
