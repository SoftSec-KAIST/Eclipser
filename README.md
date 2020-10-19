Eclipser
========

Eclipser is a binary-based fuzz testing tool that improves upon classic
coverage-based fuzzing by leveraging a novel technique called *grey-box concolic
testing*. The details of the technique can be found in our paper "Grey-box
Concolic Testing on Binary Code", which is published in ICSE 2019.

# Installation

Eclipser currently supports Linux ELF binaries, and has been tested on Debian
and Ubuntu. Eclipser is written in F# and runs on .NET Core. Also, Eclipser
performs program instrumentation based on QEMU code.

1. Install dependencies

```
$ (First, you should add deb-src entries to /etc/apt/sources.list)
$ sudo apt-get update
$ sudo apt-get build-dep qemu
$ sudo apt-get install libtool libtool-bin wget automake autoconf bison gdb
```

2. Install .NET Core

Installation differs for each Linux distribution, so please refer to this
[link](https://www.microsoft.com/net/download/linux-package-manager/ubuntu18-04/sdk-current).
Choose your Linux distribution and version from the tab and follow the
instructions.

3. Clone and build Eclipser

```
$ git clone https://github.com/SoftSec-KAIST/Eclipser
$ cd Eclipser
$ make
```

# Usage

- Running with AFL

Starting from v2.0, Eclipser only performs grey-box concolic testing for test
case generation and relies on AFL to perform random-based fuzzing (for the
context of this decision, refer to [Eclipser v2.0](#eclipser-v20) section
below). Therefore, you should first launch AFL instances in parallel mode.
Although it is possible to run Eclipser alone, it is intended only for simple
testing and not for realistic fuzzing.

```
$ AFL_DIR/afl-fuzz -i <seed dir> -o <sync dir> -M <ID 1> \
  -f <input file to fuzz> -Q -- <target program cmdline>
$ AFL_DIR/afl-fuzz -i <seed dir> -o <sync dir> -S <ID 2> \
  -f <input file to fuzz>  -Q -- <target program cmdline>
$ dotnet ECLIPSER_DIR/build/Eclipser.dll \
  -t <timeout (sec)> -i <seed dir (optional)> -s <sync dir> -o <output dir> \
  -p <target program> --arg <target program cmdline> -f <input file to fuzz>
```

We note that the output directory for Eclipser should be placed under the
synchronization directory (e.g. `-s ../syncdir -o ../syncdir/eclipser-output`).
AFL will automatically create an output directory under the synchronization
directory, using its specified ID. This way, Eclipser and AFL will share test
cases with each other. To obtain the final result of the fuzzing, retrieve all
the test cases under `<sync dir>/*/queue/` and `<sync dir>/*/crashes/`.

Similarly to AFL, Eclipser will fuzz the file input specified by `-f` option, and
fuzz the standard input when `-f` option is not provided. However, Eclipser does
not support `@@` syntax used by AFL.

- Examples

You can find simple example programs and their fuzzing scripts in
[examples](./examples) directory. An example script to run Eclipser with AFL can
be found [here](examples/test_integerate.sh). Note that we create separate
working directories for each AFL instance and Eclipser in this script. This is
to prevent the instances from using the same input file path for fuzzing.

- Other options for fuzzing

You can get the full list of Eclipser's options and their descriptions by
running the following command.

```
$ dotnet build/Eclipser.dll --help
```

# Eclipser v2.0

Originally, Eclipser had its own simplified random-based fuzzing module, instead
of relying on AFL. This was to support fuzzing multiple input sources (e.g.
command-line arguments, standard input, and file input) within a single fuzzer
run. We needed this feature for the comparison against KLEE on Coreutils
benchmark, which was one of the main experimental targets in our paper.

However, as Eclipser is more often compared with other fuzzing tools, we abandon
this feature and focus on fuzzing a single input source, as most fuzzers do. We
also largely updated the command line interface of Eclipser accordingly. We note
that you can still checkout v1.0 code from our repository to reproduce the
Coreutils experiment result.

By focusing on fuzzing a single input source, we can now use AFL to perform
random-based fuzzing. For this, from v2.0 Eclipser runs in parallel with AFL, as
described above. This way, we can benefit from various features offered by AFL,
such as source-based instrumentation, persistent mode, and deterministic mode.
Still, the core architecture of Eclipser remains the same: it complements
random-based fuzzing with our grey-box concolic testing technique.

# Docker

We also provide a Docker image to run the experiments in our paper, in
[Eclipser-Artifact](https://github.com/SoftSec-KAIST/Eclipser-Artifact)
repository. Note that this image uses Eclipser v0.1, since the image was
built for the artifact evaluation of ICSE 2019.

# Supported Architectures

Eclipser currently supports x86 and x64 architecture binaries. We internally
have a branch that supports ARM architecture, but do not plan to open source it.
In default, Eclipser assumes that the target program is an x64 binary. If you
want to fuzz an x86 binary, you should provide `--architecture x86` option to
Eclipser.

# Citation

Please consider citing our paper (ICSE 2019):
```bibtex
@INPROCEEDINGS{choi:icse:2019,
  author = {Jaeseung Choi and Joonun Jang and Choongwoo Han and Sang Kil Cha},
  title = {Grey-box Concolic Testing on Binary Code},
  booktitle = {Proceedings of the International Conference on Software Engineering},
  pages = {736--747},
  year = 2019
}
```
