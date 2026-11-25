cmake_minimum_required(VERSION 3.20)
project(ImageConverterShellExtension LANGUAGES CXX)

# Right-click "빠른 이미지 포맷 변환" menu for Explorer.
#   core/  portable C++17 logic, unit-tested on Linux and Windows
#   win/   the in-process COM DLL (Windows only)
# See README.md for the architecture and the contracts with the engine CLI and the app.

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

option(QIC_WARNINGS_AS_ERRORS "Treat compiler warnings as errors" ON)

if(MSVC)
  # Static CRT: the DLL is loaded into explorer.exe and must not depend on a VC++ redistributable.
  set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
  add_compile_options(/W4 /utf-8 /permissive- /EHsc /Zc:__cplusplus)
  add_compile_definitions(UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
  if(QIC_WARNINGS_AS_ERRORS)
    add_compile_options(/WX)
  endif()
  # Exploit mitigations for code that runs inside explorer.exe:
  #   /sdl /GS        extra security checks and stack cookies
  #   /guard:cf       Control Flow Guard
  #   /CETCOMPAT      hardware shadow stack (Intel CET / AMD shadow stack)
  #   /DYNAMICBASE /HIGHENTROPYVA /NXCOMPAT  ASLR and DEP (explicit, so a toolchain change cannot drop them)
  add_compile_options(/sdl /GS /guard:cf)
  add_link_options(/guard:cf /DYNAMICBASE /HIGHENTROPYVA /NXCOMPAT /CETCOMPAT)
else()
  add_compile_options(-Wall -Wextra -Wpedantic -Wshadow -Wconversion -Wno-sign-conversion)
  if(WIN32)
    add_compile_definitions(UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
  endif()
  if(QIC_WARNINGS_AS_ERRORS)
    add_compile_options(-Werror)
  endif()
endif()

add_library(qic_core STATIC
  core/text.cpp
  core/formats.cpp
  core/json.cpp
  core/settings.cpp
  core/command_line.cpp
  core/menu_model.cpp
  core/registration.cpp
)
target_include_directories(qic_core PUBLIC ${CMAKE_CURRENT_SOURCE_DIR})

enable_testing()

add_executable(qic_core_tests
  tests/test_main.cpp
  tests/text_test.cpp
  tests/formats_test.cpp
  tests/json_test.cpp
  tests/settings_test.cpp
  tests/command_line_test.cpp
  tests/menu_model_test.cpp
  tests/registration_test.cpp
)
target_link_libraries(qic_core_tests PRIVATE qic_core)
target_include_directories(qic_core_tests PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/tests)
# Lets tests check files outside this folder (the MSIX manifest fragment).
target_compile_definitions(qic_core_tests PRIVATE
  QIC_REPO_ROOT="${CMAKE_CURRENT_SOURCE_DIR}/../..")
add_test(NAME core_tests COMMAND qic_core_tests)

if(WIN32)
  # The shell extension itself.
  add_library(ImageConverter.ShellExtension SHARED
    win/dllmain.cpp
    win/explorer_command.cpp
    win/launcher.cpp
    win/module.cpp
    win/exports.def
  )
  target_link_libraries(ImageConverter.ShellExtension PRIVATE
    qic_core shell32 shlwapi ole32 advapi32 user32)
  set_target_properties(ImageConverter.ShellExtension PROPERTIES PREFIX "")
  if(MSVC)
    # DLL planting guard: the DLL's own imports resolve only from System32, never from the
    # current folder of whatever process loaded it (LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x800).
    target_link_options(ImageConverter.ShellExtension PRIVATE /DEPENDENTLOADFLAG:0x800)
  endif()
  if(MINGW)
    # No libstdc++/libgcc DLLs next to a DLL that Explorer loads.
    target_link_options(ImageConverter.ShellExtension PRIVATE -static -static-libgcc -static-libstdc++)
  endif()

  # Fake engine CLI and app for the smoke tests, copied next to the DLL under the real names.
  add_executable(qic_fake_cli tests/win/fake_cli.cpp)
  target_link_libraries(qic_fake_cli PRIVATE qic_core)
  if(MINGW)
    target_link_options(qic_fake_cli PRIVATE -municode -static)
  endif()
  add_custom_command(TARGET qic_fake_cli POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_if_different $<TARGET_FILE:qic_fake_cli>
            $<TARGET_FILE_DIR:ImageConverter.ShellExtension>/imgconv.exe
    COMMAND ${CMAKE_COMMAND} -E copy_if_different $<TARGET_FILE:qic_fake_cli>
            $<TARGET_FILE_DIR:ImageConverter.ShellExtension>/ImageConverter.exe)

  add_executable(qic_shell_smoke_tests tests/test_main.cpp tests/win/smoke_test.cpp)
  target_link_libraries(qic_shell_smoke_tests PRIVATE qic_core shell32 ole32 advapi32)
  target_include_directories(qic_shell_smoke_tests PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/tests)
  if(MINGW)
    target_link_options(qic_shell_smoke_tests PRIVATE -static)
  endif()
  add_dependencies(qic_shell_smoke_tests ImageConverter.ShellExtension qic_fake_cli)
  add_test(NAME shell_smoke_tests COMMAND qic_shell_smoke_tests
           WORKING_DIRECTORY $<TARGET_FILE_DIR:ImageConverter.ShellExtension>)
endif()
