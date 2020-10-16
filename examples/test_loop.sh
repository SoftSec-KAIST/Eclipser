#!/bin/bash

# Tests if Eclipser can handle path explosion in a loop. Eclipser should be able
# to find a test case containing \x64\x63\x62\x61.
gcc loop.c -o loop.bin -static -g || exit 1
rm -rf box
mkdir box
cd box
dotnet ../../build/Eclipser.dll \
  -p ../loop.bin -t 45 -v 2 -o output -f input --arg input --nsolve 10
