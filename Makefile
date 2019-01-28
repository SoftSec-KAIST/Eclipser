BUILDDIR=$(shell pwd)/build
SHDIR=$(shell pwd)/Instrumentor/sparsehash
QEMUDIR=$(shell pwd)/Instrumentor/qemu

all: builddir $(SHDIR)/.compiled $(QEMUDIR)/.compiled $(BUILDDIR)/libexec.dll Eclipser

clean:
	rm -f $(SHDIR)/.compiled
	rm -rf $(SHDIR)/build
	rm -f $(QEMUDIR)/.compiled
	rm -rf $(QEMUDIR)/qemu-2.3.0
	rm -rf $(QEMUDIR)/qemu-2.3.0-*
	rm -rf $(BUILDDIR)

builddir:
	@mkdir -p $(BUILDDIR)

$(SHDIR)/.compiled:
	cd $(SHDIR) && ./build_sparsehash.sh
	@touch $@

$(QEMUDIR)/.compiled:
	cd $(QEMUDIR) && ./build_qemu_support.sh
	@touch $@

Eclipser:
	dotnet build -c Release -o $(BUILDDIR)

$(BUILDDIR)/libexec.dll: src/Executor/libexec.c
	gcc -O3 -shared -fPIC $^ -o $@

.PHONY: all clean Eclipser
