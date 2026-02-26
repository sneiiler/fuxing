# -----------------------------------------------------------------------------
# File: CMakeLists.txt
# Author: Yin, Kaifeng
# Email: ykf@live.cn
# Date: 2026-02-10
# Version: 1.0.0
# Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
# -----------------------------------------------------------------------------

# Collect source files from subdirectories (plugin_cmakelists_template only
# globs the top-level source/ directory, so we must list subdirectory files
# explicitly via ADDITIONAL_SOURCES).

set(AICHAT_TOOL_SOURCES
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/AiChatToolTypes.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/IToolHandler.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/AiChatFileEditUtils.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/AiChatFileEditUtils.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/AiChatToolRegistry.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/AiChatToolExecutor.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/AiChatToolExecutor.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/ReadFileToolHandler.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/ReadFileToolHandler.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/WriteFileToolHandler.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/WriteFileToolHandler.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/ReplaceInFileToolHandler.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/ReplaceInFileToolHandler.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/DeleteFileToolHandler.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/DeleteFileToolHandler.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/InsertBeforeToolHandler.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/InsertBeforeToolHandler.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/InsertAfterToolHandler.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/InsertAfterToolHandler.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/ListFilesToolHandler.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/ListFilesToolHandler.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/SearchFilesToolHandler.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/SearchFilesToolHandler.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/LoadSkillToolHandler.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/LoadSkillToolHandler.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/SetStartupFileToolHandler.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/SetStartupFileToolHandler.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/RunTestsToolHandler.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/RunTestsToolHandler.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/ExecuteCommandToolHandler.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/ExecuteCommandToolHandler.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/ListCodeDefinitionNamesToolHandler.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/ListCodeDefinitionNamesToolHandler.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/AskQuestionToolHandler.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/AskQuestionToolHandler.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/AttemptCompletionToolHandler.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/AttemptCompletionToolHandler.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/NormalizeEncodingToolHandler.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/NormalizeEncodingToolHandler.cpp
)

set(AICHAT_CORE_SOURCES
    ${CMAKE_CURRENT_SOURCE_DIR}/core/AiChatService.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/core/AiChatService.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/core/AiChatTask.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/core/AiChatTask.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/core/AiChatPromptEngine.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/core/AiChatPromptEngine.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/core/AiChatPathAccessManager.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/core/AiChatPathAccessManager.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/core/AiChatAutoApprovePolicy.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/core/AiChatAutoApprovePolicy.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/core/AiChatSkillManager.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/core/AiChatSkillManager.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/core/AiChatSessionManager.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/core/AiChatSessionManager.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/core/AiChatContextManager.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/core/AiChatContextManager.cpp
)

set(AICHAT_BRIDGE_SOURCES
    ${CMAKE_CURRENT_SOURCE_DIR}/bridge/AiChatEditorBridge.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/bridge/AiChatEditorBridge.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/bridge/AiChatProjectBridge.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/bridge/AiChatProjectBridge.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/bridge/AiChatProcessRunner.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/bridge/AiChatProcessRunner.cpp
)

plugin_cmakelists_template(WizAIChat wizard
                           QT_MODULES Network
                           LIBRARIES wizard_core wkf_common
                           ADDITIONAL_SOURCES
                              ${AICHAT_TOOL_SOURCES}
                              ${AICHAT_CORE_SOURCES}
                              ${AICHAT_BRIDGE_SOURCES}
                              ${CMAKE_CURRENT_SOURCE_DIR}/AiChatEncodingUtils.hpp
                              ${CMAKE_CURRENT_SOURCE_DIR}/AiChatEncodingUtils.cpp
                              ${CMAKE_CURRENT_SOURCE_DIR}/AiChatInlineReviewBar.hpp
                              ${CMAKE_CURRENT_SOURCE_DIR}/AiChatInlineReviewBar.cpp)

if (MSVC)
    target_compile_options(WizAIChat PRIVATE /utf-8)
endif()

# Ensure subdirectory headers are on the include path
target_include_directories(WizAIChat PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}
    ${CMAKE_CURRENT_SOURCE_DIR}/tools
    ${CMAKE_CURRENT_SOURCE_DIR}/core
    ${CMAKE_CURRENT_SOURCE_DIR}/bridge
    ${CMAKE_SOURCE_DIR}/swdev/src/tools/util/source
)
