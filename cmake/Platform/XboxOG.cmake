# The original Xbox as a CMake platform (CMAKE_SYSTEM_NAME XboxOG, set by RxdkToolchain.cmake).
# Its compiler target is i686-pc-windows-gnu, but it is not Windows: WIN32 stays unset, so a
# project takes its own Xbox branch (if(XBOXOG)) rather than its Windows one.

set(XBOXOG TRUE)

# Static libraries and executables are named as rxdk.project.json builds name them.
set(CMAKE_STATIC_LIBRARY_PREFIX "")
set(CMAKE_STATIC_LIBRARY_SUFFIX ".lib")
set(CMAKE_EXECUTABLE_SUFFIX ".exe")
set(CMAKE_C_OUTPUT_EXTENSION ".obj")
set(CMAKE_CXX_OUTPUT_EXTENSION ".obj")
set(CMAKE_ASM_OUTPUT_EXTENSION ".obj")
set(CMAKE_FIND_LIBRARY_PREFIXES "" "lib")
set(CMAKE_FIND_LIBRARY_SUFFIXES ".lib")
# An SDK library is named as its file (target_link_libraries(game libd3d8 libxapi) links
# libd3d8.lib and libxapi.lib); Rxdk.Cli link-exe finds it in the SDK.
set(CMAKE_LINK_LIBRARY_FLAG "")
set(CMAKE_LINK_LIBRARY_SUFFIX ".lib")
set_property(GLOBAL PROPERTY TARGET_SUPPORTS_SHARED_LIBS FALSE)

foreach(_lang C CXX)
  # As rxdk build: the language stated with -x, and the flags (which carry the SDK include
  # directory) ahead of the project's includes, so the SDK's headers win over same-named ones.
  if(_lang STREQUAL "C")
    set(_x "c")
  else()
    set(_x "c++")
  endif()
  set(CMAKE_${_lang}_COMPILE_OBJECT
      "<CMAKE_${_lang}_COMPILER> <DEFINES> <FLAGS> <INCLUDES> -x ${_x} -o <OBJECT> -c <SOURCE>")
  set(CMAKE_${_lang}_CREATE_STATIC_LIBRARY "<CMAKE_AR> rcs <TARGET> <OBJECTS>")
  set(CMAKE_${_lang}_ARCHIVE_CREATE "<CMAKE_AR> rcs <TARGET> <OBJECTS>")
  set(CMAKE_${_lang}_ARCHIVE_APPEND "<CMAKE_AR> rs <TARGET> <OBJECTS>")
  set(CMAKE_${_lang}_ARCHIVE_FINISH "")
  # The title link (startup, SDK library order, compiler-rt, the .eh_frame brackets) is the
  # engine's, run by Rxdk.Cli; long object lists go through a response file.
  set(CMAKE_${_lang}_LINK_EXECUTABLE
      "\"${RXDK_CLI}\" link-exe <LINK_FLAGS> -o <TARGET> <OBJECTS> <LINK_LIBRARIES>")
  set(CMAKE_${_lang}_USE_RESPONSE_FILE_FOR_OBJECTS 1)
  set(CMAKE_${_lang}_RESPONSE_FILE_LINK_FLAG "@")
endforeach()

include("${CMAKE_CURRENT_LIST_DIR}/../RxdkTitle.cmake")
