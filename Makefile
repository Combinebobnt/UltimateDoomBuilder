
BUILDTYPE ?= Release
NATIVE_LIB := Source/Native/Release/libBuilderNative.so
NATIVE_SRC := $(wildcard Source/Native/*.cpp Source/Native/OpenGL/*.cpp Source/Native/OpenGL/gl_load/*.c Source/Native/VPO/*.cpp)
NATIVE_HDR := $(wildcard Source/Native/*.h Source/Native/OpenGL/*.h Source/Native/OpenGL/gl_load/*.h Source/Native/VPO/*.h)

.PHONY: all run linux mac builder nativemac native clean

# msbuild wipes Build/ before it compiles, so nothing may write there alongside it
.NOTPARALLEL:

all: linux

run:
	cd Build && mono Builder.exe

linux: builder native

mac: builder nativemac

builder:
	msbuild /nologo /verbosity:minimal -p:Configuration=$(BUILDTYPE) BuilderMono.sln
	cp builder.sh Build/builder
	chmod +x Build/builder
	if [ -f $(NATIVE_LIB) ]; then cp $(NATIVE_LIB) Build/; fi

nativemac:
	g++ -std=c++14 -O2 --shared -g3 -o Build/libBuilderNative.so -fPIC -I Source/Native Source/Native/*.cpp Source/Native/OpenGL/*.cpp Source/Native/OpenGL/gl_load/*.c Source/Native/VPO/*.cpp -ldl

native: $(NATIVE_LIB)
	cp $(NATIVE_LIB) Build/

$(NATIVE_LIB): $(NATIVE_SRC) $(NATIVE_HDR)
	mkdir -p $(@D)
	g++ -std=c++14 -O2 --shared -g3 -o $@ -fPIC -I Source/Native $(NATIVE_SRC) -DUDB_LINUX=1 -lX11 -lXfixes -ldl

clean:
	msbuild /nologo /verbosity:minimal /t:Clean -p:Configuration=$(BUILDTYPE) BuilderMono.sln
	rm -rf Build Source/Native/Release
