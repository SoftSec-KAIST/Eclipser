#!/bin/sh
#
# Sparsehash library build script

SH_URL=https://github.com/sparsehash/sparsehash/archive/sparsehash-2.0.3.tar.gz
TARFILE=sparsehash-2.0.3.tar.gz
SRCDIR_ORIG=sparsehash-sparsehash-2.0.3
SRCDIR=sparsehash-2.0.3
BUILDDIR=build

echo "==============================="
echo "Sparsehash library build script"
echo "==============================="
echo

if [ ! -f $TARFILE ]; then
  echo "[*] Downloading sparsehash from the web..."
  wget -O $TARFILE -- "$SH_URL" || exit 1
fi

echo "[*] Validating checksum of $TARFILE"
sha512sum -c sparsehash-2.0.3.tar.gz.sha512 || \
  (echo "[*] SHA512 checksum mismatch on $TARFILE (download error)" && exit 1)

echo "[*] Uncompressing archive"
rm -rf $SRCDIR_ORIG
rm -rf $SRCDIR
tar -xzf $TARFILE && mv $SRCDIR_ORIG $SRCDIR || exit 1

echo "[*] Building sparsehash"
rm -rf $BUILDDIR
mkdir $BUILDDIR && cd $BUILDDIR && ../$SRCDIR/configure && make || exit 1
