#!/bin/bash

# Tests if Eclipser can fuzz standard input.
gcc stdin.c -o stdin.bin -static -g || exit 1
rm -rf box
mkdir box
cd box
dotnet ../../build/Eclipser.dll -p ../stdin.bin -t 5 -v 1 -o output
