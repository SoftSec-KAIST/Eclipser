#!/bin/bash

# Tests if Eclipser can handle nested conditional branches. Eclipser should be
# able to find test cases containing \x53, \x72\x71, and \x41\x42\x43\x44.
gcc nested.c -o nested.bin -static -g || exit 1
rm -rf box
mkdir box
cd box
dotnet ../../build/Eclipser.dll \
  -p ../nested.bin -t 20 -v 2 -o output -f input --arg input
