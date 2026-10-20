# Sets a language's per-configuration flags to exactly the engine's optimize-mode flags (from
# RxdkToolchain.cmake), replacing the defaults CMake's Clang support adds before this file runs:
# rxdk build adds no -DNDEBUG, and no second -g or -O.
#
#   Debug          -> Debug
#   Release        -> ReleaseFast
#   RelWithDebInfo -> ReleaseSafe
#   MinSizeRel     -> ReleaseSmall
macro(__rxdk_config_flags lang)
  set(CMAKE_${lang}_FLAGS_DEBUG_INIT "${_rxdk_debug}")
  set(CMAKE_${lang}_FLAGS_RELEASE_INIT "${_rxdk_release}")
  set(CMAKE_${lang}_FLAGS_RELWITHDEBINFO_INIT "${_rxdk_relwithdebinfo}")
  set(CMAKE_${lang}_FLAGS_MINSIZEREL_INIT "${_rxdk_minsizerel}")
endmacro()
