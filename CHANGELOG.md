# Eclipser Change Log

## v2.0

* Simplify architecture by removing multiple input source fuzzing. This feature
  has been supported for the comparison against KLEE.
* Remove our own random fuzzing module, and support integration with AFL.
* Fix QEMU instrumentation code (update to QEMU-2.10.0, fix bugs, optimize).
* Add a feature to decide execution timeout automatically.
* Clean up codes.
* Update command line interface.
* Update test examples.

## v1.1

* Fix initial seed set handling.
* Use edge coverage instead of node coverage.
* Fix the default parameters for maximum file/stdin length.

## v1.0

* Stop polluting '/tmp/' directory and keep the intermediate files internally.
* Replace command line option '--fixfilepath' into '--filepath'. However, we
  still accept '--fixfilepath' option silently, for backward compatibility.
* Optimize QEMU tracer for program instrumentation. Previously, coverage tracer
  memorized node coverage information in a file, but now we use shared memory to
  reduce overhead.
* Make Eclipser strictly comply with the given time limit.
* Clean up overall codes. Especially, grey-box concolic testing module was
  extensively refactored.
* Remove message logging for debugging.
* Make examples more compact.

## v0.1

First public release of prototype.

