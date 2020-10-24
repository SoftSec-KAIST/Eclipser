#!/bin/bash

# Tests if Eclipser can cooperate well with AFL. First, Eclipser should be able
# to find a test case containing \x44\x43\x42\x41. Then, AFL should import this
# one and generate a new test case with an extended length. Finally, Eclipser
# should take this back, and find a crash that contains \x64\x63\x62\x61.

if [ -z "$1" ]
then
  echo "Should provide AFL root directory as an argument"
  exit 1
fi

if [ ! -d $1 ]
then
  echo "Cannot find AFL root directory path $1"
  exit 1
fi

gcc length.c -o length.bin -static -g || exit 1
rm -rf box
mkdir box
cd box
mkdir seeds
echo "" > seeds/empty
mkdir afl-master-box
mkdir afl-slave-box
mkdir eclipser-box
mkdir syncdir

# Launch master and slave AFLs.
cd afl-master-box
cp ../../length.bin ./
echo "Start master AFL"
CMD="timeout 20 $1/afl-fuzz -i ../seeds -o ../syncdir/ -M afl-master -f input -Q -- ./length.bin input > log.txt"
echo "#!/bin/bash" > run_master.sh
echo $CMD >> run_master.sh
chmod 755 run_master.sh
./run_master.sh &
echo "Launched master AFL"

cd ../afl-slave-box
cp ../../length.bin ./
echo "Start slave AFL"
CMD="timeout 20 $1/afl-fuzz -i ../seeds -o ../syncdir/ -S afl-slave -f input -Q -- ./length.bin input > log.txt"
echo "#!/bin/bash" > run_slave.sh
echo $CMD >> run_slave.sh
chmod 755 run_slave.sh
./run_slave.sh &
echo "Launched slave AFL"

# Now launch Eclipser.
cd ../eclipser-box
cp ../../length.bin ./
dotnet ../../../build/Eclipser.dll \
  -t 20 -v 2 -s ../syncdir -o ../syncdir/eclipser-output \
  -p ./length.bin --arg input -f input 
