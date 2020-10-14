#!/bin/bash

# Tests if Eclipser can solve linear branch conditions.
gcc linear.c -o linear.bin -static -g || exit 1
rm -rf box
mkdir box
cd box
dotnet ../../build/Eclipser.dll \
  -p ../linear.bin -t 5 -v 1 -o output -f input --arg input
