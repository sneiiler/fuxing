// -----------------------------------------------------------------------------
// File: AiChatPromptEngine.cpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#include "AiChatPromptEngine.hpp"
#include "../bridge/AiChatEditorBridge.hpp"
#include "../bridge/AiChatProjectBridge.hpp"

#include <QCoreApplication>
#include <QDir>
#include <QFileInfo>
#include <QSysInfo>
#include <QDateTime>

namespace AiChat
{

QString PromptEngine::BuildSystemPrompt() const
{
   QStringList sections;

   // 1. Role identity (short)
   sections << BuildRoleSection();

   // 1b. Tone and style (conciseness, objectivity)
   sections << BuildToneSection();

   // 2. User custom instructions (high priority - placed early to avoid
   //    "lost in the middle" effect)
   if (!mCustomInstructions.isEmpty()) {
      sections << QStringLiteral("====\n\nUSER CUSTOM INSTRUCTIONS\n\n%1").arg(mCustomInstructions);
   }

   // 3. Dynamic environment context
   sections << BuildEnvironmentSection();

   // 4. What the assistant can do
   sections << BuildCapabilitiesSection();

   // 5. Tool catalogue & usage strategy
   sections << BuildToolRulesSection();

   // 6. File-editing guidance
   sections << BuildEditingFilesSection();

   // 7. Skills (catalog + active content)
   const QString skillsSection = BuildSkillsSection();
   if (!skillsSection.isEmpty()) {
      sections << skillsSection;
   }

   // 8. Behavioural rules (single source of truth)
   sections << BuildRulesSection();

   // 9. How to approach a task
   sections << BuildObjectiveSection();

   return sections.join(QStringLiteral("\n\n"));
}

// ---------------------------------------------------------------------------
// Section: ROLE (1 sentence - matches Cline's agent_role pattern)
// ---------------------------------------------------------------------------

QString PromptEngine::BuildRoleSection() const
{
   if (!mPersona.isEmpty()) {
      return mPersona;
   }

   return QStringLiteral(
      "You are AIChat, an AI coding assistant in the AFSIM Wizard editor.");
}

// ---------------------------------------------------------------------------
// Section: TONE AND STYLE
// ---------------------------------------------------------------------------

QString PromptEngine::BuildToneSection() const
{
   return QStringLiteral(
      "====\n\nTONE AND STYLE\n\n"
      "- Be concise and direct. Answer in 1-4 sentences unless detail is needed.\n"
      "- No preamble (\"Certainly!\") or postamble (\"Let me know if...\").\n"
      "- Prioritize technical accuracy. Respectful correction > false agreement.\n"
      "- Match the user's language for conversation. Use Markdown but no emojis.\n"
      "- NEVER give time estimates.\n");
}

// ---------------------------------------------------------------------------
// Section: CAPABILITIES (what the assistant can do)
// ---------------------------------------------------------------------------

QString PromptEngine::BuildCapabilitiesSection() const
{
   return QStringLiteral(
      "====\n\nCAPABILITIES\n\n"
      "- Tool definitions describe each tool's parameters \u2014 read the schema.\n"
      "- Combine tools strategically: list_files -> search_files -> read_file -> "
      "replace_in_file. After refactoring, search_files for affected references.\n"
      "- Workspace structure is shown in ENVIRONMENT INFORMATION.");
}

// ---------------------------------------------------------------------------
// Section: OBJECTIVE (how to approach a task - single source for
//          <thinking>, attempt_completion, and feedback rules)
// ---------------------------------------------------------------------------

QString PromptEngine::BuildObjectiveSection() const
{
   return QStringLiteral(
      "====\n\nOBJECTIVE\n\n"
      "You accomplish tasks iteratively:\n\n"
      "1. Analyze the task and set clear goals in logical order.\n"
      "2. **Load skills FIRST** — if the task involves writing or editing scenario files, "
      "call load_skill(\"base\") plus any relevant domain skills BEFORE doing anything "
      "else. This is mandatory; see SKILLS section.\n"
      "3. Work through goals sequentially, one tool call at a time.\n"
      "4. Before any tool call, reason in <thinking></thinking> tags:\n"
      "   - Review Project Root Contents in ENVIRONMENT INFORMATION.\n"
      "   - For a NEW file: verify the target directory fits the project structure; "
      "call list_files if unsure.\n"
      "   - Verify all required parameters are present.\n"
      "5. After completing the task, use attempt_completion with a brief summary. "
      "For Q&A, answer directly \u2014 do NOT use attempt_completion.\n"
      "6. Prefer action and reasonable assumptions over asking. Only use ask_question "
      "when a required parameter is truly ambiguous.\n\n"
      "## Avoid Over-Engineering\n"
      "- Only make changes directly requested or clearly necessary.\n"
      "- Do NOT add features, comments, abstractions, or error handling beyond what "
      "was asked.\n");
}

QString PromptEngine::BuildEnvironmentSection() const
{
   QStringList info;

   info << QStringLiteral("====\n\nENVIRONMENT INFORMATION");

   // OS info
   info << QStringLiteral("- Operating System: %1 %2")
              .arg(QSysInfo::prettyProductName(), QSysInfo::currentCpuArchitecture());

   // Current date/time
   info << QStringLiteral("- Current Time: %1")
              .arg(QDateTime::currentDateTime().toString(Qt::ISODate));

   // Project information
   QString projectName = ProjectBridge::GetProjectName();
   QString workspaceRoot  = ProjectBridge::GetWorkspaceRoot();
   if (!projectName.isEmpty()) {
      info << QStringLiteral("- Project Name: %1").arg(projectName);
   }
   if (!workspaceRoot.isEmpty()) {
      info << QStringLiteral("- Workspace Root: %1").arg(workspaceRoot);
   }

   // Startup files
   QStringList startupFiles = ProjectBridge::GetStartupFiles();
   if (!startupFiles.isEmpty()) {
      info << QStringLiteral("- Startup Files: %1").arg(startupFiles.join(", "));
   }

   // Project file tree snapshot (top-level) — helps the model stay oriented
   // after context truncation, without needing a list_files call.
   if (!workspaceRoot.isEmpty()) {
      QDir rootDir(workspaceRoot);
      QStringList entries = rootDir.entryList(
         QDir::AllEntries | QDir::NoDotAndDotDot, QDir::DirsFirst | QDir::Name);
      if (!entries.isEmpty()) {
         // Mark directories with trailing '/'
         QStringList decorated;
         for (const auto& e : entries) {
            if (rootDir.exists(e) && QFileInfo(rootDir.filePath(e)).isDir()) {
               decorated << (e + QStringLiteral("/"));
            } else {
               decorated << e;
            }
         }
         info << QStringLiteral("- Project Root Contents: %1").arg(decorated.join(", "));
      }
   }

   // Currently active editor
   QString currentFile = EditorBridge::GetCurrentFilePath();
   if (!currentFile.isEmpty()) {
      info << QStringLiteral("- Currently Open File: %1").arg(currentFile);
      int curLine = EditorBridge::GetCurrentLine();
      if (curLine > 0) {
         info << QStringLiteral("- Current Cursor Line: %1").arg(curLine);
      }
   }

   // Open files list
   QStringList openFiles = EditorBridge::GetOpenFiles();
   if (!openFiles.isEmpty()) {
      info << QStringLiteral("- Open Editor Tabs: %1").arg(openFiles.join(", "));
   }

   // AFSIM installation directory
   const QString appDir = QCoreApplication::applicationDirPath();
   info << QStringLiteral("- AFSIM Install Directory (bin/): %1").arg(QDir::toNativeSeparators(appDir));

   // Additional environment info from dynamic context
   if (!mEnvironmentInfo.isEmpty()) {
      info << mEnvironmentInfo;
   }

   return info.join('\n');
}

// ---------------------------------------------------------------------------
// Section: TOOL USE  (usage strategy only - parameter details come from
//                     the function-calling schema, NOT duplicated here)
// ---------------------------------------------------------------------------

QString PromptEngine::BuildToolRulesSection() const
{
   return QStringLiteral(
      "====\n\nTOOL USE\n\n"

      "## Tool Preference\n"
      "- ALWAYS use file tools (read_file, write_to_file, replace_in_file, etc.) "
      "instead of execute_command for file operations.\n"
      "- Use search_files for text search, list_files for directory listing.\n"
      "- Reserve execute_command for commands with no dedicated tool (git, build).\n\n"

      "## Read-Before-Edit\n"
      "- NEVER edit a file you haven't read. read_file first; user-provided content "
      "counts as read.\n\n"

      "## Shell\n"
      "- Default shell: cmd.exe. Do NOT prefix with 'cmd /c'.\n"
      "- For PowerShell: powershell -NoProfile -Command \"...\"\n"
      "- Prefer non-interactive commands (-y, --yes flags).\n\n"

      "## Scenario Validation\n"
      "After creating or modifying scenario files:\n"
      "  1. If needed, call set_startup_file.\n"
      "  2. Call run_tests to validate.\n"
      "Result interpretation:\n"
      "- 'Simulation complete' -> SUCCESS.\n"
      "- Large negative exit code + 'Simulation complete' -> VALID (DDS Gateway crash, "
      "do NOT retry).\n"
      "- 'Unable to open file' -> non-fatal directory issue.\n");
}

// ---------------------------------------------------------------------------
// Section: EDITING FILES
// ---------------------------------------------------------------------------

QString PromptEngine::BuildEditingFilesSection() const
{
   return QStringLiteral(
      "====\n\nEDITING FILES\n\n"
      "- **replace_in_file**: default for edits. Safer and more precise.\n"
      "- **write_to_file**: only for new files or after 3 failed replace_in_file attempts.\n"
      "- **insert_before / insert_after**: add text around a known anchor pattern.\n\n"

      "## replace_in_file Rules\n"
      "- SEARCH blocks must contain COMPLETE lines (no partial matches).\n"
      "- **Batch all changes to the same file into ONE replace_in_file call** with "
      "multiple SEARCH/REPLACE blocks listed in file order. Making N separate calls "
      "when one call with N blocks works is wasteful and slow.\n"
      "- For bulk renaming (e.g., renaming 16 platform names), put ALL 16 "
      "SEARCH/REPLACE blocks in a single diff parameter — NOT 16 separate calls.\n"
      "- On failure the file is auto-reverted. Re-read and retry with precise SEARCH.\n\n"

      "## New File Checklist\n"
      "Before write_to_file for a NEW file:\n"
      "  1. Path must be RELATIVE to workspace root.\n"
      "  2. Parent directory must fit the project structure (platforms/, scenarios/, etc.).\n"
      "  3. Confirm no duplicate file exists elsewhere (list_files / search_files).\n\n"

      "## Write Verification\n"
      "- Same error after write_to_file? Re-read the file to confirm the write took "
      "effect.\n");
}

// ---------------------------------------------------------------------------
// Section: RULES  (single source of truth - no duplicates elsewhere)
// ---------------------------------------------------------------------------

QString PromptEngine::BuildRulesSection() const
{
   return QStringLiteral(
      "====\n\nRULES\n\n"
      "- Working directory = workspace root in ENVIRONMENT INFORMATION. "
      "It is FIXED for the entire session.\n"
      "- Modify files directly with tools; do not display changes first.\n"
      "- After scenario changes, validate (see Scenario Validation in TOOL USE).\n\n"

      "## Conventions\n"
      "- Before writing new files, examine existing project files for patterns and style.\n"
      "- NEVER assume a WSF type or command exists. Verify with loaded skill content "
      "or search_files first.\n"
      "- NEVER write scenario code without first loading relevant skills (see SKILLS "
      "section). Skipping skill loading leads to invalid syntax.\n\n"

      "## Path Restrictions\n"
      "- All file paths MUST be relative to the workspace root.\n"
      "- NEVER write files outside the workspace root.\n\n"

      "## File Creation Discipline\n"
      "- NEVER create the same file in multiple locations. Edit in place.\n"
      "- 'Cannot open file' errors: check include paths relative to WORKSPACE ROOT.\n"
      "- Wrong directory? delete_file the old copy, write_to_file in the correct one.\n\n"

      "## Content Restrictions (scenario files: .txt, .wsf, .script)\n"
      "- ALL scenario file content must be pure ASCII (0x20-0x7E + tab/newline). "
      "No Unicode.\n"
      "- Chat messages CAN use Chinese/Unicode; only file content must be ASCII.\n"
      "- Scenario files must use .txt extension. Never insert null bytes.\n");
}

QString PromptEngine::BuildSkillsSection() const
{
   if (mSkillCatalogSummary.isEmpty() && mActiveSkillContent.isEmpty()) {
      return {};
   }

   QStringList lines;
   lines << QStringLiteral("====\n\nSKILLS");
   lines << QString();

   if (!mSkillCatalogSummary.isEmpty()) {
      lines << QStringLiteral(
         "## Skill Catalog\n"
         "\n"
         "Available skills (call load_skill(\"<name>\") to load):\n");
      lines << mSkillCatalogSummary;
      lines << QString();
      lines << QStringLiteral(
         "## Mandatory Skill Loading\n"
         "\n"
         "CRITICAL: You MUST load relevant skills BEFORE writing or editing any "
         "scenario file. Skills contain the ONLY authoritative reference for AFSIM "
         "syntax and types. Writing scenario code without loading skills will produce "
         "incorrect syntax and invalid types.\n"
         "\n"
         "Rules:\n"
         "1. **Always load \"base\" first.** It contains core syntax, project structure, "
         "and file templates required for every AFSIM task.\n"
         "2. **Load domain skills before writing domain-specific code.** Match the task "
         "to skill descriptions: sensor work -> \"sensor\", weapons -> \"weapon\", "
         "satellites -> \"satellite\", platforms -> \"platform\", etc.\n"
         "3. **Load skills in your FIRST tool call**, before read_file, list_files, or "
         "any file writes. You may load multiple skills in a single turn.\n"
         "4. The returned content is the AUTHORITATIVE reference. Follow it directly; "
         "do NOT invent WSF types, parameters, or syntax not found in loaded skills.\n"
         "5. Do NOT re-load a skill already loaded this session.\n"
         "6. If no skill matches the task, proceed without.\n"
         "\n"
         "Example — user asks to create a satellite with a sensor:\n"
         "  -> load_skill(\"base\"), load_skill(\"satellite\"), load_skill(\"sensor\")\n"
         "  -> THEN read files, write scenario code\n");
   }

   if (!mActiveSkillContent.isEmpty()) {
      lines << QString();
      lines << mActiveSkillContent;
   }

   return lines.join('\n');
}

// BuildOutputRulesSection removed - its content has been merged into
// BuildObjectiveSection (tone/language rules) and BuildRulesSection
// (environment_details handling) to eliminate duplication.

// ============================================================================
// BuildSummarizePrompt -- for auto-condense (Phase 4)
// ============================================================================

QString PromptEngine::BuildSummarizePrompt() const
{
   return QStringLiteral(
      "You are a conversation summarizer. Your task is to produce a structured, "
      "comprehensive summary of the conversation history provided to you.\n\n"
      "The summary MUST cover all 10 sections below. For each section, write a "
      "concise but complete paragraph. If a section is not applicable, write "
      "'N/A' for that section.\n\n"
      "## Section 1: Primary Request and Intent\n"
      "What is the user's original request? What are they trying to accomplish?\n\n"
      "## Section 2: Key Technical Concepts\n"
      "What technical frameworks, languages, APIs, or domain concepts are involved? "
      "(e.g., AFSIM scenario types, C++ patterns, specific WSF blocks)\n\n"
      "## Section 3: Files and Code Segments\n"
      "List all files that were read, created, or modified, with a brief note about "
      "the purpose of each change. Include file paths.\n\n"
      "## Section 4: Problem-Solving Record\n"
      "What problems were encountered? What solutions were tried? What worked and "
      "what didn't? Include error messages and fixes.\n\n"
      "## Section 5: Pending Tasks\n"
      "What tasks remain incomplete? What was planned but not yet done?\n\n"
      "## Section 6: Task Evolution\n"
      "How has the task changed from the original request? Any scope changes or "
      "new requirements that emerged?\n\n"
      "## Section 7: Current Work State\n"
      "What was the assistant working on most recently? What is the current state "
      "of the project/code?\n\n"
      "## Section 8: Next Steps\n"
      "What should happen next? What are the immediate next actions?\n\n"
      "## Section 9: Critical Files List\n"
      "List the file paths (up to 8) that are most important for continuing the task. "
      "These files should be re-read after resuming.\n"
      "Format: one path per line, prefixed with '- '\n\n"
      "## Section 10: Recent User Focus\n"
      "What was the user's most recent message about? What are they currently "
      "focused on?\n\n"
      "IMPORTANT:\n"
      "- Be thorough but concise. Each section should be 2-5 sentences.\n"
      "- Preserve all specific technical details: file paths, variable names, "
      "function signatures, error messages, configuration values.\n"
      "- Do NOT omit any information that would be needed to continue the task.\n"
      "- Write in the same language the user used (Chinese if they used Chinese).\n"
      "- Output ONLY the summary sections, no extra commentary.\n"
      "- Do NOT call any tools or functions. Produce ONLY plain text output.");
}

// ============================================================================
// BuildContinuationPrompt -- injected after condensation
// ============================================================================

QString PromptEngine::BuildContinuationPrompt(const QString& aSummary)
{
   return QStringLiteral(
      "[CONVERSATION CONTEXT SUMMARY]\n\n"
      "The previous conversation history has been condensed into a summary "
      "to manage context length. The original conversation has been replaced "
      "with this summary. Continue assisting the user based on the context below.\n\n"
      "---\n\n"
      "%1\n\n"
      "---\n\n"
      "[Continue assisting the user from where the conversation left off. "
      "Do not re-introduce yourself. Any previously activated skills remain "
      "available in the system prompt - you do not need to reload them. "
      "If critical files from Section 9 need to be re-read, do so before "
      "proceeding with the next task.]")
      .arg(aSummary);
}

} // namespace AiChat
