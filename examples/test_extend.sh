#!/bin/bash

if [ -z "$1" ]
then
  echo "Should provide AFL root directory as argument"
  exit 1
fi

if [ ! -d $1 ]
then
  echo "Cannot find AFL root directory path $1"
  exit 1
fi

gcc extend.c -o extend.bin -static -g || exit 1
rm -rf box
mkdir box
cd box
mkdir input
echo "" > input/empty
mkdir afl-box
mkdir eclipser-box
mkdir syncdir

# Launch master and slave AFLs.
cd afl-box
cp ../../extend.bin ./
echo "Start master AFL"
CMD="timeout 20 $1/afl-fuzz -i ../input -o ../syncdir/ -M afl-master -Q -- ./extend.bin @@ > log.txt"
echo "#!/bin/bash" > run_master.sh
echo $CMD >> run_master.sh
chmod 755 run_master.sh
./run_master.sh &
echo "Launched master AFL"

echo "Start slave AFL"
CMD="timeout 20 $1/afl-fuzz -i ../input -o ../syncdir/ -S afl-slave -Q -- ./extend.bin @@ > log.txt"
echo "#!/bin/bash" > run_slave.sh
echo $CMD >> run_slave.sh
chmod 755 run_slave.sh
./run_slave.sh &
echo "Launched slave AFL"

# Now launch Eclipser.
cd ../eclipser-box
cp ../../extend.bin ./
dotnet ../../../build/Eclipser.dll \
  -t 20 -v 1 -s ../syncdir -o ../syncdir/eclipser-output \
  -p ./extend.bin --arg input -f input 
