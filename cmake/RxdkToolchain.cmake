# CMake toolchain for original Xbox titles, built with RXDK.
#
#   cmake -G Ninja -DCMAKE_TOOLCHAIN_FILE=<RXDK tools>/cmake/RxdkToolchain.cmake -DCMAKE_BUILD_TYPE=Release <src>
#
# The compile flags, the link and the XBE come from RXDK's engine (Rxdk.Cli), the same code
# `rxdk build` runs, so a CMake build matches an rxdk.project.json one. CMake builds objects
# and static libraries itself; an executable links through `Rxdk.Cli link-exe`, and
# rxdk_add_xbe(<target> ...) turns it into an .xbe (and optionally an ISO).
#
# Settings (cache variables):
#   RXDK_CLI            Rxdk.Cli to use (default: the one next to this file's folder)
#   RXDK_CXX_STANDARD   C++ standard for -std= (default c++23, as rxdk.project.json)
#   RXDK_CXX_EXCEPTIONS C++ exceptions (default ON, as rxdk.project.json)
#   RXDK_TLS_RETRY      Recompile a thread_local translation unit with line tables only when
#                       full debug info would break its link, as `rxdk build` does (default: on
#                       for Debug and RelWithDebInfo).

cmake_minimum_required(VERSION 3.21)

set(CMAKE_SYSTEM_NAME XboxOG)
set(CMAKE_SYSTEM_PROCESSOR i686)
set(CMAKE_SYSTEM_VERSION 1)
# Platform/XboxOG.cmake lives next to this file.
list(APPEND CMAKE_MODULE_PATH "${CMAKE_CURRENT_LIST_DIR}")

if(NOT RXDK_CLI)
  find_program(RXDK_CLI NAMES Rxdk.Cli HINTS "${CMAKE_CURRENT_LIST_DIR}/.." NO_DEFAULT_PATH)
endif()
if(NOT RXDK_CLI)
  message(FATAL_ERROR "RXDK: Rxdk.Cli not found next to ${CMAKE_CURRENT_LIST_DIR}. Set RXDK_CLI.")
endif()
set(RXDK_CLI "${RXDK_CLI}" CACHE FILEPATH "RXDK engine command line (Rxdk.Cli)")
# CMake's compiler checks configure their own projects with this file; hand them the settings.
list(APPEND CMAKE_TRY_COMPILE_PLATFORM_VARIABLES RXDK_CLI RXDK_CXX_STANDARD RXDK_CXX_EXCEPTIONS RXDK_TLS_RETRY)
set(RXDK_CXX_STANDARD "c++23" CACHE STRING "C++ standard (-std=) for Xbox C++ code")
option(RXDK_CXX_EXCEPTIONS "C++ exceptions in Xbox C++ code" ON)

# Paths the engine resolves: the RXDK clang, llvm-ar, the SDK, imagebld and xdvdfs.
execute_process(COMMAND "${RXDK_CLI}" toolchain-info
                OUTPUT_VARIABLE _rxdk_info RESULT_VARIABLE _rxdk_rc ERROR_VARIABLE _rxdk_err)
if(NOT _rxdk_rc EQUAL 0)
  message(FATAL_ERROR "RXDK: `Rxdk.Cli toolchain-info` failed: ${_rxdk_err}")
endif()
string(REPLACE "\r" "" _rxdk_info "${_rxdk_info}")
string(REPLACE "\n" ";" _rxdk_info "${_rxdk_info}")
foreach(_line IN LISTS _rxdk_info)
  if(_line MATCHES "^(RXDK_[A-Z_]+)=(.*)$")
    set(${CMAKE_MATCH_1} "${CMAKE_MATCH_2}")
  endif()
endforeach()

# Flags from the engine, one per line, joined into a CMake flags string (quoting any with spaces).
function(_rxdk_engine_flags out)
  execute_process(COMMAND "${RXDK_CLI}" compile-args ${ARGN}
                  OUTPUT_VARIABLE _out RESULT_VARIABLE _rc ERROR_VARIABLE _err)
  if(NOT _rc EQUAL 0)
    message(FATAL_ERROR "RXDK: `Rxdk.Cli compile-args ${ARGN}` failed: ${_err}")
  endif()
  string(REPLACE "\r" "" _out "${_out}")
  string(REPLACE "\n" ";" _out "${_out}")
  set(_flags "")
  foreach(_f IN LISTS _out)
    if(_f STREQUAL "")
      continue()
    endif()
    if(_f MATCHES " ")
      set(_f "\"${_f}\"")
    endif()
    string(APPEND _flags " ${_f}")
  endforeach()
  string(STRIP "${_flags}" _flags)
  set(${out} "${_flags}" PARENT_SCOPE)
endfunction()

set(_rxdk_cxx_args --lang c++ --cpp-std "${RXDK_CXX_STANDARD}")
if(NOT RXDK_CXX_EXCEPTIONS)
  list(APPEND _rxdk_cxx_args --no-exceptions)
endif()
_rxdk_engine_flags(_rxdk_c_flags --lang c)
_rxdk_engine_flags(_rxdk_cxx_flags ${_rxdk_cxx_args})
_rxdk_engine_flags(_rxdk_debug --optimize Debug)
_rxdk_engine_flags(_rxdk_release --optimize ReleaseFast)
_rxdk_engine_flags(_rxdk_relwithdebinfo --optimize ReleaseSafe)
_rxdk_engine_flags(_rxdk_minsizerel --optimize ReleaseSmall)

set(CMAKE_C_COMPILER "${RXDK_CC}")
set(CMAKE_CXX_COMPILER "${RXDK_CC}")
set(CMAKE_ASM_COMPILER "${RXDK_CC}")
set(CMAKE_AR "${RXDK_AR}" CACHE FILEPATH "RXDK llvm-ar")

set(CMAKE_C_FLAGS_INIT "${_rxdk_c_flags}")
set(CMAKE_CXX_FLAGS_INIT "${_rxdk_cxx_flags}")
# The per-configuration flags are set in Platform/XboxOG-Clang-<lang>.cmake, which CMake reads
# after its own Clang defaults (-g, -O3 -DNDEBUG...), so they stay exactly the engine's.
# The link carries debug info when the configuration keeps it, as `rxdk build`.
set(CMAKE_EXE_LINKER_FLAGS_DEBUG_INIT "--debug")
set(CMAKE_EXE_LINKER_FLAGS_RELWITHDEBINFO_INIT "--debug")

# Nothing links without the SDK's startup and libraries, so CMake's compiler checks build a
# static library instead of an executable.
set(CMAKE_TRY_COMPILE_TARGET_TYPE STATIC_LIBRARY)

# Full debug info and thread_local don't link together (see RXDK_TLS_RETRY above).
# A single-configuration build defaults to Debug, as rxdk.project.json does: without a build type
# CMake would compile with no optimize flags at all, which no `rxdk build` configuration does.
get_property(_rxdk_multi_config GLOBAL PROPERTY GENERATOR_IS_MULTI_CONFIG)
if(NOT _rxdk_multi_config AND NOT CMAKE_BUILD_TYPE AND NOT DEFINED ENV{CMAKE_BUILD_TYPE})
  set(CMAKE_BUILD_TYPE Debug CACHE STRING "Debug, Release (ReleaseFast), RelWithDebInfo (ReleaseSafe) or MinSizeRel (ReleaseSmall)")
endif()

if(NOT DEFINED RXDK_TLS_RETRY)
  # The retry only acts on a full-debug object that uses thread_local, so it is on wherever a
  # configuration may have full debug info.
  if(_rxdk_multi_config OR CMAKE_BUILD_TYPE MATCHES "^(Debug|RelWithDebInfo)$")
    set(RXDK_TLS_RETRY ON)
  else()
    set(RXDK_TLS_RETRY OFF)
  endif()
endif()
if(RXDK_TLS_RETRY)
  set(CMAKE_C_COMPILER_LAUNCHER "${RXDK_CLI};compile-launch;--")
  set(CMAKE_CXX_COMPILER_LAUNCHER "${RXDK_CLI};compile-launch;--")
endif()
