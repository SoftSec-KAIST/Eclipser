QEMU_VERSION=2.10.0
BUILDDIR=$(shell pwd)/build
QEMUDIR=$(shell pwd)/Instrumentor

all: $(BUILDDIR) $(QEMUDIR)/.compiled Eclipser

x86: $(BUILDDIR) $(QEMUDIR)/.compiled_x86 Eclipser

x64: $(BUILDDIR) $(QEMUDIR)/.compiled_x64 Eclipser

clean:
	rm -f $(QEMUDIR)/.prepared
	rm -f $(QEMUDIR)/.compiled
	rm -f $(QEMUDIR)/.compiled_x86
	rm -f $(QEMUDIR)/.compiled_x64
	rm -rf $(QEMUDIR)/qemu-${QEMU_VERSION}
	rm -rf $(QEMUDIR)/qemu-${QEMU_VERSION}-*
	rm -rf $(BUILDDIR)

$(BUILDDIR):
	mkdir -p $(BUILDDIR)

$(QEMUDIR)/.prepared:
	cd $(QEMUDIR) && ./prepare_qemu.sh
	@touch $@

$(QEMUDIR)/.compiled: $(QEMUDIR)/.compiled_x86 $(QEMUDIR)/.compiled_x64
	@touch $@

$(QEMUDIR)/.compiled_x86: $(QEMUDIR)/.prepared
	cd $(QEMUDIR) && ./build_qemu_x86.sh
	@touch $@

$(QEMUDIR)/.compiled_x64: $(QEMUDIR)/.prepared
	cd $(QEMUDIR) && ./build_qemu_x64.sh
	@touch $@

$(BUILDDIR)/libexec.dll: src/Core/libexec.c
	gcc -O3 -shared -fPIC $< -o $@

Eclipser: $(BUILDDIR)/libexec.dll
	dotnet build -c Release -o $(BUILDDIR)

.PHONY: all x86 x64 clean Eclipser
