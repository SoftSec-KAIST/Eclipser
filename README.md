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

- Basic options

The basic usage of Eclipser is as follow. Note that you should provide `fuzz`
keyword before other options.

```
$ dotnet build/Eclipser.dll fuzz \
    -p <target program path> -v <verbosity level> -t <timeout second> \
    -o <output directory> --src <'arg'|'file'|'stdin'|'auto'>
```

This command will fuzz the specified target program for the given amount of
time, and store the fuzzing outputs (i.e. test cases and crashes) into the
output directory. The last argument `--src ...` specifies target program's input
source to fuzz, and further explanation about this option is given below.

- Fuzzing command line arguments of a target program

By providing `--src arg` option to Eclipser, you can fuzz command-line argument
input of the target program. You can specify the number of arguments and the
maximum length of each argument string with `--maxarglen` option.

For example, the following command will fuzz target program 'example.bin' by
mutating command line argument inputs. A generated test input can have up to
three argument strings (i.e. argc <= 3), and the length of the first argument
string is limited up to 8 bytes, while the second and third arguments are
confined to 4-bytes strings.

```
$ dotnet build/Eclipser.dll fuzz \
    -p example.bin -v 1 -t 10 -o outdir --src arg --maxarglen 8 4 4
```

- Fuzzing a file input of a target program

By providing `--src file` option to Eclipser, you can fuzz a file input of a
target program. You can specify the command line argument of target program with
`--initarg` option, and specify the input file name with `-f` option.

For example, consider a target program that takes in input file via "--input"
option. Using the following command, you can fuzz the file input of this program
and limit the file input length up to 8 bytes. Currently we support only one
file input.

```
$ dotnet build/Eclipser.dll fuzz \
    -p example.bin -v 1 -t 10 -o outdir \
    --src file --initarg "--input foo" -f foo --maxfilelen 8
```

You may also want to provide initial seed inputs for the fuzzing. You can use
`-i <seed directory>` option to provide initial seed input files for the target
program.

- Fuzzing the standard input of a target program

By providing `--src stdin` to Eclipser, you can fuzz the standard input of a
target program. Currently, we assume a standard input to be a single string, and
do not consider cases where a sequence of string is provided as a standard input
stream of the target program.

For example, the following command will fuzz target program 'example.bin' by
mutating its standard input. The length of standard input is confined up to 8
bytes.

```
$ dotnet build/Eclipser.dll fuzz \
    -p example.bin -v 1 -t 10 -o outdir --src stdin --maxstdinlen 8
```

- Fuzzing multiple input sources.

Eclipser also supports a mode that automatically identifies and fuzz input
sources. When you provide `--src auto` option to Eclipser, it will first start
by fuzzing command line argument of the target program. Then, it will trace
system call invocations during the program execution, to identify the use of
standard input or file input. Once identified, Eclipser will automatically fuzz
these input sources as well. As in previous examples, you can specify the
maximum lengths with `--max*` options.

```
$ dotnet build/Eclipser.dll fuzz \
    -p example.bin -v 1 -t 10 -o outdir \
    --src auto --maxarglen 8 4 4 --maxfilelen 8 --maxstdinlen 8
```

Note: Eclipser identifies a file input as an input source only if the file name
matches one of the argument string. This means if a target program reads in a
configuration file from a fixed path, this file will not be considered as a
input source to fuzz in `--src auto` mode.

- Other options for fuzzing

You can get the full list of Eclipser's options and their detailed descriptions
by running the following command.

```
$ dotnet build/Eclipser.dll fuzz --help
```

# Test case decoding utility

Eclipser internally store test cases in its custom JSON format. For usability,
we provide a utility that decodes these JSON format test cases in a specified
directory.

For example, suppose that you fuzzed a target program with the following
command.

```
$ dotnet build/Eclipser.dll fuzz -p example.bin -v 1 -t 10 -o outdir --src auto
```

Then, Eclipser will store generated test cases in `outdir/testcase`, and store
found crash inputs in `outdir/crash`. Now, to decode the test cases, you can run
the following command. Note the `decode` keyword is used in place of `fuzz`.

```
$ dotnet build/Eclipser.dll decode -i outdir/testcase -o decoded_testcase
```

Then, Eclipser will store the decoded raw string inputs in subdirectories, as
shown below.

```
$ ls decoded_testcase
decoded_args  decoded_files  decoded_paths  decoded_stdins
$ xxd decoded_testcase/decoded_files/tc-0
00000000: 4142 4242 4242 4242 4242 4242 4242 4242  ABBBBBBBBBBBBBBB
$ xxd decoded_testcase/decoded_files/tc-1
00000000: 6142 4242 4242 4242 4242 4242 4242 4242  aBBBBBBBBBBBBBBB
(...)
```

# Examples

You can find simple example programs, along with testing scripts to fuzz these
programs, in [examples](./examples) directory.

# Docker

We also provide a Docker image to run the experiments in our paper, in
[Eclipser-Artifact](https://github.com/SoftSec-KAIST/Eclipser-Artifact)
repository.

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
