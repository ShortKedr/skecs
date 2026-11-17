cmake_minimum_required(VERSION 3.20)

if(NOT DEFINED CMAKE_OSX_DEPLOYMENT_TARGET)
    if(CMAKE_SYSTEM_NAME STREQUAL "iOS")
        set(CMAKE_OSX_DEPLOYMENT_TARGET "14.0" CACHE STRING "Minimum deployment target")
    else()
        set(CMAKE_OSX_DEPLOYMENT_TARGET "11.0" CACHE STRING "Minimum deployment target")
    endif()
endif()

project(SkecsGaMetalNative LANGUAGES NONE)
if(NOT CMAKE_SYSTEM_NAME MATCHES "^(Darwin|iOS)$")
    message(FATAL_ERROR "The GA Metal bridge requires macOS or iOS and the Apple SDK.")
endif()
enable_language(OBJCXX)

find_library(FOUNDATION_FRAMEWORK Foundation REQUIRED)
find_library(METAL_FRAMEWORK Metal REQUIRED)

add_library(ga-metal-native SHARED GaMetalNative.mm)
set_target_properties(ga-metal-native PROPERTIES
    OBJCXX_STANDARD 17
    OBJCXX_STANDARD_REQUIRED YES
    OBJCXX_VISIBILITY_PRESET hidden
    VISIBILITY_INLINES_HIDDEN YES)
target_compile_options(ga-metal-native PRIVATE -fobjc-arc -fblocks -Wall -Wextra)
target_link_libraries(ga-metal-native PRIVATE "${FOUNDATION_FRAMEWORK}" "${METAL_FRAMEWORK}")

if(CMAKE_SYSTEM_NAME STREQUAL "iOS")
    set_target_properties(ga-metal-native PROPERTIES
        FRAMEWORK YES
        MACOSX_FRAMEWORK_IDENTIFIER org.skecs.ga.metal-native)
endif()
