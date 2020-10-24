#!/bin/bash

# Tests if Eclipser can correctly identify coverage gain even if the program
# exits with a timeout. Eclipser should be able to find a test case containing
# \x44\x43\x42\x41.
gcc timeout.c -o timeout.bin -static -g || exit 1
rm -rf box
mkdir box
cd box
dotnet ../../build/Eclipser.dll \
  -p ../timeout.bin -t 5 -v 2 -o output -f input --arg input
