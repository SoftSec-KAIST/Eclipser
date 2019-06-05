# Eclipser Change Log

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

