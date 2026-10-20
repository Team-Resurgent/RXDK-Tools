# rxdk_add_xbe(<target>
#              [TITLE_NAME <name>] [TITLE_ID <0xXXXXXXXX>] [STACK <bytes>]
#              [IMAGE_BUILD_JSON <file>]
#              [ISO] [ISO_NAME <name>] [DEPLOY <[dest=]source>...])
#
# After <target> (an add_executable) links, builds <target>.xbe next to it with imagebld, as
# `rxdk build` does. IMAGE_BUILD_JSON takes the same settings as rxdk.project.json's
# "imageBuild" (title image, regions, flags...); TITLE_NAME, TITLE_ID and STACK override it.
#
# ISO also packs the XBE, and any DEPLOY files or folders, into XISO/<ISO_NAME or target>.iso in
# the current binary directory (per configuration with a multi-config generator), staging the
# disc's files in Build/<name> beside it. DEPLOY entries are rxdk.project.json "deployPaths": a
# source relative to the current source directory, optionally with its place on the disc first
# (dest=source). The ISO is packed again whenever the XBE or a deployed file changes; files added
# to a deployed folder are noticed when CMake next configures (they are globbed CONFIGURE_DEPENDS).
function(rxdk_add_xbe target)
  cmake_parse_arguments(PARSE_ARGV 1 X "ISO" "TITLE_NAME;TITLE_ID;STACK;IMAGE_BUILD_JSON;ISO_NAME" "DEPLOY")
  if(X_UNPARSED_ARGUMENTS)
    message(FATAL_ERROR "rxdk_add_xbe: unknown arguments: ${X_UNPARSED_ARGUMENTS}")
  endif()

  set(_xbe_cmd "${RXDK_CLI}" make-xbe --exe "$<TARGET_FILE:${target}>")
  if(X_IMAGE_BUILD_JSON)
    get_filename_component(_json "${X_IMAGE_BUILD_JSON}" ABSOLUTE BASE_DIR "${CMAKE_CURRENT_SOURCE_DIR}")
    list(APPEND _xbe_cmd --image-build "${_json}")
  endif()
  if(X_TITLE_NAME)
    list(APPEND _xbe_cmd --title-name "${X_TITLE_NAME}")
  endif()
  if(X_TITLE_ID)
    list(APPEND _xbe_cmd --title-id "${X_TITLE_ID}")
  endif()
  if(X_STACK)
    list(APPEND _xbe_cmd --stack "${X_STACK}")
  endif()
  if(X_IMAGE_BUILD_JSON)
    # Editing the settings relinks, which builds the XBE again.
    set_property(TARGET ${target} APPEND PROPERTY LINK_DEPENDS "${_json}")
  endif()
  add_custom_command(TARGET ${target} POST_BUILD
                     COMMAND ${_xbe_cmd}
                     WORKING_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}"
                     COMMENT "Building ${target}.xbe"
                     VERBATIM)

  if(X_ISO)
    set(_name "${X_ISO_NAME}")
    if(NOT _name)
      set(_name "${target}")
    endif()
    set(_out_dir "${CMAKE_CURRENT_BINARY_DIR}")
    get_property(_multi_config GLOBAL PROPERTY GENERATOR_IS_MULTI_CONFIG)
    if(_multi_config)
      string(APPEND _out_dir "/$<CONFIG>")
    endif()
    set(_iso "${_out_dir}/XISO/${_name}.iso")
    set(_iso_cmd "${RXDK_CLI}" make-iso
        --xbe "$<TARGET_FILE_DIR:${target}>/$<TARGET_FILE_BASE_NAME:${target}>.xbe"
        --name "${_name}" --out-dir "${_out_dir}"
        --project-root "${CMAKE_CURRENT_SOURCE_DIR}")
    set(_deps ${target})
    foreach(_d IN LISTS X_DEPLOY)
      list(APPEND _iso_cmd --deploy "${_d}")
      # The source is what follows the first '=' (the part before it is the place on the disc).
      string(FIND "${_d}" "=" _eq)
      if(_eq GREATER 0)
        math(EXPR _eq "${_eq} + 1")
        string(SUBSTRING "${_d}" ${_eq} -1 _src)
      else()
        set(_src "${_d}")
      endif()
      get_filename_component(_src "${_src}" ABSOLUTE BASE_DIR "${CMAKE_CURRENT_SOURCE_DIR}")
      if(IS_DIRECTORY "${_src}")
        file(GLOB_RECURSE _files CONFIGURE_DEPENDS LIST_DIRECTORIES false "${_src}/*")
        list(APPEND _deps ${_files})
      elseif(EXISTS "${_src}")
        list(APPEND _deps "${_src}")
      endif()
    endforeach()
    # The executable target as a dependency also makes the ISO depend on its file, so a relink
    # (which rebuilds the XBE) packs again.
    add_custom_command(OUTPUT "${_iso}"
                       COMMAND ${_iso_cmd}
                       DEPENDS ${_deps}
                       WORKING_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}"
                       COMMENT "Packing ${_name}.iso"
                       VERBATIM)
    add_custom_target(${target}_iso ALL DEPENDS "${_iso}")
  endif()
endfunction()
