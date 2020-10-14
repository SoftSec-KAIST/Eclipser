#!/bin/bash

# Tests if Eclipser can run without fork server as well.
gcc linear.c -o linear.bin -static -g || exit 1
rm -rf box
mkdir box
cd box
dotnet ../../build/Eclipser.dll \
  -p ../linear.bin -t 5 -v 1 -o output -f input --arg input --noforkserver
