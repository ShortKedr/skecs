if(EXPECTED_SHA256)
    file(SHA256 "${INPUT}" actual_sha)
    if(NOT actual_sha STREQUAL EXPECTED_SHA256)
        message(FATAL_ERROR "Prebuilt startup SPIR-V SHA-256 mismatch")
    endif()
endif()

file(SIZE "${INPUT}" byte_count)
math(EXPR spare_bytes "${byte_count} % 4")
file(READ "${INPUT}" magic OFFSET 0 LIMIT 4 HEX)
if(byte_count LESS 20 OR NOT spare_bytes EQUAL 0 OR NOT magic STREQUAL "03022307")
    message(FATAL_ERROR "Startup input must be a little-endian SPIR-V module")
endif()

if(SPIRV_VALIDATOR)
    execute_process(COMMAND "${SPIRV_VALIDATOR}" --target-env vulkan1.1 "${INPUT}"
        COMMAND_ERROR_IS_FATAL ANY)
endif()

file(READ "${INPUT}" bytes HEX)
string(REGEX REPLACE "(..)(..)(..)(..)" "    0x\\4\\3\\2\\1u,\n" words "${bytes}")
file(WRITE "${OUTPUT}" "constexpr uint32_t startupSpirv[] = {\n${words}};\n")
