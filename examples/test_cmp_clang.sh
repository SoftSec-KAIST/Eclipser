#!/bin/bash

# Grey-box concolic should find more than 9 file input test cases that have node
# coverage gain.
clang cmp.c -o cmp.bin -static -g || exit 1
rm -rf box
mkdir box
cd box
dotnet ../../build/Eclipser.dll \
  -p ../cmp.bin -t 5 -v 1 -o output -f input --arg input
