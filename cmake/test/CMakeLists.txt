# A CMake-built title that checks the RXDK toolchain end to end: C and C++ sources, a static
# library target, C++ exceptions and RTTI, thread_local (the emulated-TLS retry in Debug), and the
# XBE and ISO steps. It prints [T] lines to the debug output and "[T] ALLDONE" when finished.
#
#   cmake -G Ninja -DCMAKE_TOOLCHAIN_FILE=<RXDK tools>/cmake/RxdkToolchain.cmake -DCMAKE_BUILD_TYPE=Debug -B out/Debug
#   cmake --build out/Debug
cmake_minimum_required(VERSION 3.21)
project(RxdkCMakeTest C CXX)

if(NOT XBOXOG)
  message(FATAL_ERROR "configure with RxdkToolchain.cmake")
endif()

add_subdirectory(counter)

add_executable(cmaketest main.cpp)
# SDK libraries are named by file, as rxdk.project.json names them; Debug links the d builds.
set(_d "$<$<CONFIG:Debug>:d>")
target_link_libraries(cmaketest PRIVATE counter
                      libxbdm${_d} libxapi${_d} libkernel${_d} libc${_d} libcpp${_d} libcompat${_d})
rxdk_add_xbe(cmaketest TITLE_NAME "RXDK CMake test" TITLE_ID 0xFFFF2002 ISO DEPLOY hello.txt=data/hello.txt media=data/media)
