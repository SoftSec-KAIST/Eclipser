#!/bin/bash

# Grey-box concolic should find more than 9 file input test cases that have node
# coverage gain.
gcc cmp.c -o cmp.bin -static -g || exit 1 # -static option for easier debugging
rm -rf box
mkdir box
cd box
mkdir seeds
python -c 'print "B" * 16' > seeds/input
dotnet ../../build/Eclipser.dll \
  -p ../cmp.bin -t 5 -v 1 -i seeds -o output -f input --arg input
