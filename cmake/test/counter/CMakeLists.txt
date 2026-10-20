add_library(counter STATIC counter.c)
target_include_directories(counter PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}")
